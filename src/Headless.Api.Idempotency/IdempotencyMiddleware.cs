// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Cryptography;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Resources;
using Headless.Caching;
using Headless.Constants;
using Headless.DistributedLocks;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Idempotency;

internal sealed partial class IdempotencyMiddleware(
    IOptionsSnapshot<IdempotencyOptions> optionsSnapshot,
    ICache cache,
    ICurrentTenant currentTenant,
    IProblemDetailsCreator problemDetailsCreator,
    IClock clock,
    ICancellationTokenProvider cancellationTokenProvider,
    ILogger<IdempotencyMiddleware> logger,
    IServiceProvider serviceProvider
) : IMiddleware
{
    private readonly IProblemDetailsCreator _problemDetailsCreator = problemDetailsCreator;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<IdempotencyMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var ct = cancellationTokenProvider.Token;
        var options = _ResolveOptions(context);

        // R1: missing or whitespace key → pass-through
        var keyHeader = context.Request.Headers[options.HeaderName].LastOrDefault();
        if (string.IsNullOrWhiteSpace(keyHeader))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // R2: method not opted-in → pass-through
        if (!options.Methods.Contains(context.Request.Method))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // ShouldApply override
        if (options.ShouldApply != null && !options.ShouldApply(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Buffer body so it can be read multiple times, then scan for size/fingerprint
        context.Request.EnableBuffering(
            bufferThreshold: options.MaxBodySizeForHashing + 1,
            bufferLimit: options.MaxBodySizeForHashing + 1
        );

        var (fingerprintOrNull, oversize) = await _ComputeFingerprintAsync(context, options, ct).ConfigureAwait(false);

        if (oversize)
        {
            if (options.OversizeBehavior == OversizeBehavior.Reject)
            {
                LogBodyTooLarge(options.MaxBodySizeForHashing);
                var descriptor = IdempotencyMessageDescriber.BodyTooLarge();
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status413PayloadTooLarge,
                    Detail = descriptor.Description,
                    Extensions = { ["error"] = descriptor },
                };
                _problemDetailsCreator.Normalize(pd);
                await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
                return;
            }

            // PassThrough: log and let the request through without idempotency guarantees
            LogBodyCapPassThrough(options.MaxBodySizeForHashing);
            await next(context).ConfigureAwait(false);
            return;
        }

        var fingerprint = fingerprintOrNull!;

        // Derive cache key: idem:{tenant}:{METHOD}:{path}:{key}
        var cacheKey = _BuildCacheKey(context, options, keyHeader);

        var existing = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);

        if (existing.HasValue)
        {
            var rec = existing.Value!;

            if (rec.Kind == RecordKind.Complete && rec.Fingerprint != null && rec.Fingerprint.SequenceEqual(fingerprint))
            {
                await _ReplayAsync(context, rec, options, ct).ConfigureAwait(false);
                return;
            }

            if (rec.Kind == RecordKind.Complete)
            {
                await _WriteMismatchAsync(context, options, ct).ConfigureAwait(false);
                return;
            }

            // InFlight
            await _WriteInFlightResponseAsync(context, options, fingerprint, cacheKey, ct).ConfigureAwait(false);
            return;
        }

        // Cache miss — bounded retry loop handles the race where the winner crashes between TryInsert and finalize
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var marker = new IdempotencyRecord
            {
                Kind = RecordKind.InFlight,
                Fingerprint = fingerprint,
                CreatedAt = clock.UtcNow,
            };

            var inserted = await cache.TryInsertAsync(cacheKey, marker, options.InFlightLockTimeout + TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

            if (inserted)
            {
                await _ExecuteAndFinalizeAsync(context, next, cacheKey, fingerprint, options, ct).ConfigureAwait(false);
                return;
            }

            var racePeek = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);

            if (!racePeek.HasValue)
            {
                // Winner crashed; TTL elapsed between their TryInsert and finalize — retry insertion
                continue;
            }

            var raceRec = racePeek.Value!;

            if (raceRec.Kind == RecordKind.Complete)
            {
                if (raceRec.Fingerprint != null && raceRec.Fingerprint.SequenceEqual(fingerprint))
                {
                    await _ReplayAsync(context, raceRec, options, ct).ConfigureAwait(false);
                }
                else
                {
                    await _WriteMismatchAsync(context, options, ct).ConfigureAwait(false);
                }

                return;
            }

            await _WriteInFlightResponseAsync(context, options, fingerprint, cacheKey, ct).ConfigureAwait(false);
            return;
        }

        // Both attempts exhausted with NoValue — treat as in-flight (winner consistently crashing)
        var loopPd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlight());
        await Results.Problem(loopPd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _ReplayAsync(HttpContext context, IdempotencyRecord record, IdempotencyOptions options, CancellationToken ct)
    {
        context.Response.StatusCode = record.StatusCode;

        foreach (var (name, values) in record.Headers)
        {
            if (options.ReplayHeaderAllowlist.Contains(name))
            {
                context.Response.Headers[name] = values;
            }
        }

        context.Response.Headers[HttpHeaderNames.IdempotentReplayed] = "true";

        if (record.Body.Length > 0)
        {
            await context.Response.Body.WriteAsync(record.Body, ct).ConfigureAwait(false);
        }

        LogReplayHit(cacheKey: "[redacted]");
    }

    private async Task _WriteMismatchAsync(HttpContext context, IdempotencyOptions options, CancellationToken ct)
    {
        LogFingerprintMismatch("[redacted]");
        var descriptor = IdempotencyMessageDescriber.KeyReused();

        ProblemDetails pd = options.MismatchStatusCode == 409
            ? _problemDetailsCreator.Conflict(descriptor)
            : _problemDetailsCreator.UnprocessableEntity(new Dictionary<string, List<ErrorDescriptor>>(StringComparer.Ordinal) { ["idempotency_key"] = [descriptor] });

        await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _WriteInFlightResponseAsync(HttpContext context, IdempotencyOptions options, byte[] fingerprint, string cacheKey, CancellationToken ct)
    {
        if (options.InFlightStrategy == InFlightStrategy.WaitAndReplay)
        {
            await _WaitAndReplayAsync(context, options, fingerprint, cacheKey, ct).ConfigureAwait(false);
            return;
        }

        LogInFlightReject("[redacted]");
        var pd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlight());
        await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _WaitAndReplayAsync(HttpContext context, IdempotencyOptions options, byte[] fingerprint, string cacheKey, CancellationToken ct)
    {
        var lockProvider = _serviceProvider.GetRequiredService<IDistributedLockProvider>();
        var lockKey = $"lock:{cacheKey}";

        await using var dlock = await lockProvider.TryAcquireAsync(
            lockKey,
            timeUntilExpires: options.InFlightLockTimeout + TimeSpan.FromSeconds(5),
            acquireTimeout: options.InFlightLockTimeout,
            cancellationToken: ct
        ).ConfigureAwait(false);

        if (dlock is null)
        {
            LogInFlightTimeout("[redacted]");
            var pd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlightTimeout());
            await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var postLock = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);

        if (postLock.HasValue && postLock.Value!.Kind == RecordKind.Complete)
        {
            var rec = postLock.Value!;

            if (rec.Fingerprint != null && rec.Fingerprint.SequenceEqual(fingerprint))
            {
                await _ReplayAsync(context, rec, options, ct).ConfigureAwait(false);
                return;
            }

            await _WriteMismatchAsync(context, options, ct).ConfigureAwait(false);
            return;
        }

        // InFlight or NoValue after holding the lock → winner timed out or is still stuck
        LogInFlightTimeout("[redacted]");
        var timeoutPd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlightTimeout());
        await Results.Problem(timeoutPd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _ExecuteAndFinalizeAsync(
        HttpContext context,
        RequestDelegate next,
        string cacheKey,
        byte[] fingerprint,
        IdempotencyOptions options,
        CancellationToken ct
    )
    {
        var originalBody = context.Response.Body;
        var cap = options.MaxBodySizeForHashing;
        using var captureStream = new CaptureStream(originalBody, cap);
        context.Response.Body = captureStream;

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch
        {
            context.Response.Body = originalBody;
            await cache.RemoveAsync(cacheKey, ct).ConfigureAwait(false);
            throw;
        }

        context.Response.Body = originalBody;

        var effectivePredicate = options.ShouldCacheResponse ?? DefaultCachePredicate.Instance;
        var shouldCache = effectivePredicate(context);

        if (shouldCache && !captureStream.TruncatedCapture)
        {
            var capturedHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in context.Response.Headers)
            {
                if (options.ReplayHeaderAllowlist.Contains(header.Key))
                {
                    capturedHeaders[header.Key] = [.. header.Value.Select(v => v!)];
                }
            }

            var completeRecord = new IdempotencyRecord
            {
                Kind = RecordKind.Complete,
                StatusCode = context.Response.StatusCode,
                Headers = capturedHeaders,
                Body = captureStream.CapturedBytes,
                Fingerprint = fingerprint,
                CreatedAt = clock.UtcNow,
            };

            await cache.UpsertAsync(cacheKey, completeRecord, options.IdempotencyKeyExpiration, ct).ConfigureAwait(false);
        }
        else
        {
            await cache.RemoveAsync(cacheKey, ct).ConfigureAwait(false);
        }
    }

    private IdempotencyOptions _ResolveOptions(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>();
        if (metadata is null)
        {
            return optionsSnapshot.Value;
        }

        var cloned = _CloneOptions(optionsSnapshot.Value);
        metadata.Configure(cloned);
        return cloned;
    }

    private string _BuildCacheKey(HttpContext context, IdempotencyOptions options, string keyHeader)
    {
        if (options.KeyDeriver != null)
        {
            return options.KeyDeriver(context, keyHeader);
        }

        var tenant = currentTenant.Id ?? "";
        var method = context.Request.Method.ToUpperInvariant();
        var path = context.Request.Path.Value ?? "";
        return $"idem:{tenant}:{method}:{path}:{keyHeader}";
    }

    private static async ValueTask<(byte[]? Fingerprint, bool Oversize)> _ComputeFingerprintAsync(
        HttpContext context,
        IdempotencyOptions options,
        CancellationToken ct
    )
    {
        var body = context.Request.Body;
        var cap = options.MaxBodySizeForHashing;
        var buffer = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var totalRead = 0;

            while (totalRead <= cap)
            {
                var toRead = Math.Min(buffer.Length, cap + 1 - totalRead);
                var read = await body.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;

                if (totalRead > cap)
                {
                    body.Position = 0;
                    return (null, true);
                }

                hash.AppendData(buffer, 0, read);
            }

            body.Position = 0;

            if (options.RequestFingerprint != null)
            {
                var customFingerprint = await options.RequestFingerprint(context).ConfigureAwait(false);
                // Rewind unconditionally — the delegate may have consumed the body
                body.Position = 0;
                return (customFingerprint, false);
            }

            return (hash.GetCurrentHash(), false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IdempotencyOptions _CloneOptions(IdempotencyOptions source) => new()
    {
        IdempotencyKeyExpiration = source.IdempotencyKeyExpiration,
        HeaderName = source.HeaderName,
        Methods = new HashSet<string>(source.Methods, StringComparer.OrdinalIgnoreCase),
        InFlightStrategy = source.InFlightStrategy,
        InFlightLockTimeout = source.InFlightLockTimeout,
        MaxBodySizeForHashing = source.MaxBodySizeForHashing,
        OversizeBehavior = source.OversizeBehavior,
        MismatchStatusCode = source.MismatchStatusCode,
        ReplayHeaderAllowlist = new HashSet<string>(source.ReplayHeaderAllowlist, StringComparer.OrdinalIgnoreCase),
        ShouldCacheResponse = source.ShouldCacheResponse,
        ShouldApply = source.ShouldApply,
        KeyDeriver = source.KeyDeriver,
        RequestFingerprint = source.RequestFingerprint,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency replay hit for key {CacheKey}")]
    private partial void LogReplayHit(string cacheKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency fingerprint mismatch for key {CacheKey}")]
    private partial void LogFingerprintMismatch(string cacheKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency in-flight reject for key {CacheKey}")]
    private partial void LogInFlightReject(string cacheKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency in-flight timeout for key {CacheKey}")]
    private partial void LogInFlightTimeout(string cacheKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency request body exceeded cap {Cap}; rejecting with 413")]
    private partial void LogBodyTooLarge(int cap);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency request body exceeded cap {Cap}; passing through without idempotency guarantees")]
    private partial void LogBodyCapPassThrough(int cap);
}
