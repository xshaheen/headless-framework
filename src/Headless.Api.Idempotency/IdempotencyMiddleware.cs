// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Cryptography;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Caching;
using Headless.Constants;
using Microsoft.AspNetCore.Http;
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

        // Buffer body so it can be read multiple times, then compute fingerprint
        context.Request.EnableBuffering();
        var fingerprint = await _ComputeFingerprintAsync(context, options, ct).ConfigureAwait(false);

        // Derive cache key: idem:{tenant}:{METHOD}:{path}:{key}
        var cacheKey = _BuildCacheKey(context, options, keyHeader);

        var existing = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);

        if (existing.HasValue)
        {
            if (existing.Value!.Kind == RecordKind.Complete && existing.Value.Fingerprint != null && existing.Value.Fingerprint.SequenceEqual(fingerprint))
            {
                await _ReplayAsync(context, existing.Value!, options, ct).ConfigureAwait(false);
                return;
            }

            // Mismatch and in-flight branches handled in U7; fall through for now
        }

        // Cache miss path (or race-loss re-check)
        if (!existing.HasValue)
        {
            var marker = new IdempotencyRecord
            {
                Kind = RecordKind.InFlight,
                Fingerprint = fingerprint,
                CreatedAt = clock.UtcNow,
            };

            var inserted = await cache.TryInsertAsync(cacheKey, marker, options.InFlightLockTimeout + TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

            if (!inserted)
            {
                // Race-loss: re-read and try replay if Complete; otherwise U7 handles in-flight/mismatch
                var afterRace = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);
                if (afterRace.HasValue && afterRace.Value!.Kind == RecordKind.Complete && afterRace.Value.Fingerprint != null && afterRace.Value.Fingerprint.SequenceEqual(fingerprint))
                {
                    await _ReplayAsync(context, afterRace.Value!, options, ct).ConfigureAwait(false);
                }

                // Other states (in-flight, mismatch) handled in U7
                return;
            }

            await _ExecuteAndFinalizeAsync(context, next, cacheKey, fingerprint, options, ct).ConfigureAwait(false);
        }
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

        var shouldCache = options.ShouldCacheResponse?.Invoke(context) ?? true;

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

    private static async ValueTask<byte[]> _ComputeFingerprintAsync(HttpContext context, IdempotencyOptions options, CancellationToken ct)
    {
        if (options.RequestFingerprint != null)
        {
            return await options.RequestFingerprint(context).ConfigureAwait(false);
        }

        var body = context.Request.Body;
        var remaining = options.MaxBodySizeForHashing;
        var buffer = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            while (remaining > 0)
            {
                var toRead = Math.Min(buffer.Length, remaining);
                var read = await body.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            body.Position = 0;
            return hash.GetCurrentHash();
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
        Methods = source.Methods,
        InFlightStrategy = source.InFlightStrategy,
        InFlightLockTimeout = source.InFlightLockTimeout,
        MaxBodySizeForHashing = source.MaxBodySizeForHashing,
        OversizeBehavior = source.OversizeBehavior,
        MismatchStatusCode = source.MismatchStatusCode,
        ReplayHeaderAllowlist = source.ReplayHeaderAllowlist,
        ShouldCacheResponse = source.ShouldCacheResponse,
        ShouldApply = source.ShouldApply,
        KeyDeriver = source.KeyDeriver,
        RequestFingerprint = source.RequestFingerprint,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency replay hit for key {CacheKey}")]
    private partial void LogReplayHit(string cacheKey);
}
