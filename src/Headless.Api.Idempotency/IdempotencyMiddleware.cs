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
using Microsoft.Extensions.Primitives;

namespace Headless.Api.Idempotency;

internal sealed partial class IdempotencyMiddleware(
    IOptionsSnapshot<IdempotencyOptions> optionsSnapshot,
    ICache cache,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
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
        var appOptions = optionsSnapshot.Value;

        // Read the idempotency-key header using the app-level HeaderName *before* resolving
        // endpoint metadata. HeaderName overrides via WithIdempotency are deliberately ignored —
        // see EndpointConventionBuilderExtensions remarks.
        var headerValues = context.Request.Headers[appOptions.HeaderName];

        // R1: missing or whitespace key → pass-through
        if (headerValues.Count == 0)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var keyHeader = headerValues.LastOrDefault();
        if (string.IsNullOrWhiteSpace(keyHeader))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Now resolve per-endpoint options for everything else.
        var options = _ResolveOptions(context, appOptions);

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

        // Header value validation: length, control characters, multi-value
        if (!_ValidateKeyHeader(headerValues, keyHeader, out var malformedReason))
        {
            LogKeyMalformed(malformedReason);
            await _WriteBadRequestAsync(context, IdempotencyMessageDescriber.KeyMalformed()).ConfigureAwait(false);
            return;
        }

        // Buffer body so it can be read multiple times. We use the cap as a memory-vs-disk
        // threshold hint, but do NOT impose a hard buffer limit — PassThrough mode requires
        // downstream handlers to read bodies that legitimately exceed the cap.
        context.Request.EnableBuffering(bufferThreshold: options.MaxBodySizeForHashing + 1);

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

        // Derive cache key. The default derivation requires a tenant or authenticated user;
        // for fully anonymous routes with no KeyDeriver override, refuse to apply idempotency
        // rather than cross-pollinate cache slots between unrelated callers.
        var cacheKey = _BuildCacheKey(context, options, keyHeader);
        if (cacheKey.Length == 0)
        {
            LogSkippedNoIdentity();
            await next(context).ConfigureAwait(false);
            return;
        }

        CacheValue<IdempotencyRecord> existing;
        try
        {
            existing = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);
        }
        catch (Exception cacheEx)
        {
            LogCacheFailure("existing-record-get", cacheKey, options.OnCacheError.ToString(), cacheEx);
            if (options.OnCacheError == OnCacheErrorBehavior.Throw)
            {
                throw;
            }

            await next(context).ConfigureAwait(false);
            return;
        }

        if (existing.HasValue)
        {
            var rec = existing.Value!;

            if (rec.Kind == RecordKind.Complete)
            {
                if (rec.Fingerprint != null && _FingerprintEquals(rec.Fingerprint, fingerprint))
                {
                    await _ReplayAsync(context, rec, options, cacheKey, ct).ConfigureAwait(false);
                    return;
                }

                await _WriteMismatchAsync(context, options, cacheKey, ct).ConfigureAwait(false);
                return;
            }

            // InFlight: mismatched payloads still report 422 (mismatch), not 409 (in-flight).
            if (rec.Fingerprint != null && !_FingerprintEquals(rec.Fingerprint, fingerprint))
            {
                await _WriteMismatchAsync(context, options, cacheKey, ct).ConfigureAwait(false);
                return;
            }

            await _WriteInFlightResponseAsync(context, options, fingerprint, cacheKey, ct).ConfigureAwait(false);
            return;
        }

        // Under WaitAndReplay, acquire the distributed lock BEFORE inserting the sentinel marker.
        // Inserting the marker first creates a window in which an arriving loser sees the marker,
        // calls _WaitAndReplayAsync, and grabs the lock before the winner does — leaving the
        // winner unlocked and the loser stuck observing the InFlight marker until it times out
        // with 409 g:idempotency_in_flight_timeout. Lock-before-insert closes that window:
        // the winner holds the lock for the entire handler lifetime; concurrent losers either
        // block on the lock (WaitAndReplay) or short-circuit via _WriteInFlightResponseAsync (Reject).
        IDistributedLock? winnerLock = null;
        if (options.InFlightStrategy == InFlightStrategy.WaitAndReplay)
        {
            var lockProvider = _serviceProvider.GetRequiredService<IDistributedLockProvider>();
            try
            {
                winnerLock = await lockProvider.TryAcquireAsync(
                    $"lock:{cacheKey}",
                    timeUntilExpires: options.WinnerLockLease,
                    acquireTimeout: TimeSpan.Zero,
                    cancellationToken: ct
                ).ConfigureAwait(false);
            }
            catch (Exception lockEx)
            {
                LogLockProviderFailure("winner-tryacquire", cacheKey, options.OnCacheError.ToString(), lockEx);
                if (options.OnCacheError == OnCacheErrorBehavior.Throw)
                {
                    throw;
                }

                // FailOpen: lock provider unavailable. Bypass idempotency for this request — no
                // marker has been inserted yet (lock-before-insert ordering), so no orphan is left.
                await next(context).ConfigureAwait(false);
                return;
            }

            if (winnerLock is null)
            {
                // Another request already holds the winner-lock for this key. Defer to the
                // in-flight response path (Reject → 409, WaitAndReplay → block on existing winner).
                LogWinnerLockContended(cacheKey);
                await _WriteInFlightResponseAsync(context, options, fingerprint, cacheKey, ct).ConfigureAwait(false);
                return;
            }
        }

        try
        {
            // Cache miss — bounded retry loop handles the race where the winner crashes between TryInsert and finalize
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var marker = new IdempotencyRecord
                {
                    Kind = RecordKind.InFlight,
                    Fingerprint = fingerprint,
                    CreatedAt = clock.UtcNow,
                };

                // Sentinel TTL matches the completed-record TTL: a shorter marker TTL plus the
                // hard-coded +5s safety margin meant a slow handler could see its marker evicted
                // before finalize, opening the door to false in-flight rejects on retries.
                bool inserted;
                try
                {
                    inserted = await cache.TryInsertAsync(cacheKey, marker, options.IdempotencyKeyExpiration, ct).ConfigureAwait(false);
                }
                catch (Exception cacheEx)
                {
                    LogCacheFailure("sentinel-tryinsert", cacheKey, options.OnCacheError.ToString(), cacheEx);
                    if (options.OnCacheError == OnCacheErrorBehavior.Throw)
                    {
                        throw;
                    }

                    await next(context).ConfigureAwait(false);
                    return;
                }

                if (inserted)
                {
                    await _ExecuteAndFinalizeCoreAsync(context, next, cacheKey, fingerprint, options, ct).ConfigureAwait(false);
                    return;
                }

                CacheValue<IdempotencyRecord> racePeek;
                try
                {
                    racePeek = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);
                }
                catch (Exception cacheEx)
                {
                    LogCacheFailure("race-peek-get", cacheKey, options.OnCacheError.ToString(), cacheEx);
                    if (options.OnCacheError == OnCacheErrorBehavior.Throw)
                    {
                        throw;
                    }

                    await next(context).ConfigureAwait(false);
                    return;
                }

                if (!racePeek.HasValue)
                {
                    // Winner crashed; TTL elapsed between their TryInsert and finalize — retry insertion
                    continue;
                }

                var raceRec = racePeek.Value!;

                if (raceRec.Kind == RecordKind.Complete)
                {
                    if (raceRec.Fingerprint != null && _FingerprintEquals(raceRec.Fingerprint, fingerprint))
                    {
                        await _ReplayAsync(context, raceRec, options, cacheKey, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await _WriteMismatchAsync(context, options, cacheKey, ct).ConfigureAwait(false);
                    }

                    return;
                }

                // InFlight race winner — fingerprint mismatch still goes to 422, not 409.
                if (raceRec.Fingerprint != null && !_FingerprintEquals(raceRec.Fingerprint, fingerprint))
                {
                    await _WriteMismatchAsync(context, options, cacheKey, ct).ConfigureAwait(false);
                    return;
                }

                await _WriteInFlightResponseAsync(context, options, fingerprint, cacheKey, ct).ConfigureAwait(false);
                return;
            }

            // Both attempts exhausted with NoValue — treat as in-flight (winner consistently crashing)
            var loopPd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlight());
            await Results.Problem(loopPd).ExecuteAsync(context).ConfigureAwait(false);
        }
        finally
        {
            if (winnerLock is not null)
            {
                await winnerLock.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task _ReplayAsync(
        HttpContext context,
        IdempotencyRecord record,
        IdempotencyOptions options,
        string cacheKey,
        CancellationToken ct
    )
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException(
                "Cannot replay idempotent response: HttpResponse has already started. "
                + "Ensure UseIdempotency() is registered before any middleware that writes to the response body."
            );
        }

        context.Response.StatusCode = record.StatusCode;

        // Strip pre-existing allowlisted headers set by upstream middleware so byte-equivalent
        // replay isn't poisoned by per-request mutations (CORS, security policies). Headers
        // outside the allowlist (e.g., traceparent from logging) remain untouched. IDictionary
        // Remove on a missing key is a no-op, so iterating the allowlist avoids the double pass
        // (and List allocation) of the previous shape.
        foreach (var allowedHeader in options.ReplayHeaderAllowlist)
        {
            context.Response.Headers.Remove(allowedHeader);
        }

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

        LogReplayHit(cacheKey);
    }

    private async Task _WriteMismatchAsync(HttpContext context, IdempotencyOptions options, string cacheKey, CancellationToken ct)
    {
        LogFingerprintMismatch(cacheKey);
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

        LogInFlightReject(cacheKey);
        var pd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlight());
        await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _WriteBadRequestAsync(HttpContext context, ErrorDescriptor descriptor)
    {
        var pd = _problemDetailsCreator.BadRequest(detail: descriptor.Description, error: descriptor);
        await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _WaitAndReplayAsync(HttpContext context, IdempotencyOptions options, byte[] fingerprint, string cacheKey, CancellationToken ct)
    {
        var lockProvider = _serviceProvider.GetRequiredService<IDistributedLockProvider>();
        var lockKey = $"lock:{cacheKey}";

        // Loser path: block until the winner releases the lock (or acquireTimeout elapses).
        IDistributedLock? dlock;
        try
        {
            dlock = await lockProvider.TryAcquireAsync(
                lockKey,
                timeUntilExpires: options.WinnerLockLease,
                acquireTimeout: options.InFlightLockTimeout,
                cancellationToken: ct
            ).ConfigureAwait(false);
        }
        catch (Exception lockEx)
        {
            LogLockProviderFailure("loser-tryacquire", cacheKey, options.OnCacheError.ToString(), lockEx);
            if (options.OnCacheError == OnCacheErrorBehavior.Throw)
            {
                throw;
            }

            // FailOpen on the loser path: we cannot wait on the winner. Return a recoverable
            // 409 so the client retries — bypassing to next() would re-execute the handler.
            LogInFlightTimeout(cacheKey);
            var failOpenPd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlightTimeout());
            await Results.Problem(failOpenPd).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        if (dlock is null)
        {
            LogInFlightTimeout(cacheKey);
            var pd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlightTimeout());
            await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        await using var _ = dlock.ConfigureAwait(false);

        CacheValue<IdempotencyRecord> postLock;
        try
        {
            postLock = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);
        }
        catch (Exception cacheEx)
        {
            LogCacheFailure("post-lock-get", cacheKey, options.OnCacheError.ToString(), cacheEx);
            if (options.OnCacheError == OnCacheErrorBehavior.Throw)
            {
                throw;
            }

            // Loser path: we cannot read the winner's record. Return a recoverable 409 so the
            // client retries — bypassing to next() here would re-execute the handler and break
            // the idempotency guarantee outright.
            LogInFlightTimeout(cacheKey);
            var failOpenPd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlightTimeout());
            await Results.Problem(failOpenPd).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        if (postLock.HasValue && postLock.Value!.Kind == RecordKind.Complete)
        {
            var rec = postLock.Value!;

            if (rec.Fingerprint != null && _FingerprintEquals(rec.Fingerprint, fingerprint))
            {
                await _ReplayAsync(context, rec, options, cacheKey, ct).ConfigureAwait(false);
                return;
            }

            await _WriteMismatchAsync(context, options, cacheKey, ct).ConfigureAwait(false);
            return;
        }

        // InFlight or NoValue after holding the lock → winner timed out or is still stuck
        LogInFlightTimeout(cacheKey);
        var timeoutPd = _problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlightTimeout());
        await Results.Problem(timeoutPd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _ExecuteAndFinalizeCoreAsync(
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
            try
            {
                // Cleanup must outlive request cancellation
                await cache.RemoveAsync(cacheKey, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                LogMarkerCleanupFailed(cacheKey, cleanupEx);
            }
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
                    capturedHeaders[header.Key] = header.Value.ToArray()!;
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

            // Re-check the marker before writing the Complete record. If another writer has
            // already taken the slot (e.g., we crashed earlier and the second attempt path is
            // executing in another process), don't clobber their record.
            //
            // Post-handler site: the response is already committed, so cache exceptions cannot
            // be surfaced to the client. On exception, log and proceed with UpsertAsync —
            // losing the cache slot for a successful handler run is a worse outcome than the
            // (rare) risk of clobbering a parallel writer we cannot observe.
            CacheValue<IdempotencyRecord>? current = null;
            try
            {
                current = await cache.GetAsync<IdempotencyRecord>(cacheKey, ct).ConfigureAwait(false);
            }
            catch (Exception cacheEx)
            {
                LogCacheFailure("finalize-marker-recheck", cacheKey, options.OnCacheError.ToString(), cacheEx);
            }

            if (current is not null
                && (!current.HasValue
                    || current.Value!.Kind != RecordKind.InFlight
                    || current.Value!.Fingerprint is null
                    || !_FingerprintEquals(current.Value!.Fingerprint, fingerprint)))
            {
                LogFinalizeSkippedMarkerChanged(cacheKey);
                return;
            }

            try
            {
                await cache.UpsertAsync(cacheKey, completeRecord, options.IdempotencyKeyExpiration, ct).ConfigureAwait(false);
            }
            catch (Exception upsertEx)
            {
                LogFinalizeFailed(cacheKey, upsertEx);
                try
                {
                    // Cleanup must outlive request cancellation
                    await cache.RemoveAsync(cacheKey, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupEx)
                {
                    LogMarkerCleanupFailed(cacheKey, cleanupEx);
                }
                throw;
            }
        }
        else
        {
            try
            {
                // Cleanup must outlive request cancellation
                await cache.RemoveAsync(cacheKey, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                LogMarkerCleanupFailed(cacheKey, cleanupEx);
            }
        }
    }

    private static IdempotencyOptions _ResolveOptions(HttpContext context, IdempotencyOptions appOptions)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>();
        if (metadata is null)
        {
            return appOptions;
        }

        // Clone + merge per request. A static cache keyed by metadata captured `appOptions` from
        // the first observation, which silently ignored subsequent IOptionsSnapshot reloads for
        // endpoints with WithIdempotency(...) while plain endpoints honored reloads — an
        // asymmetric drift that was very hard to debug. Cloning per request costs a struct copy
        // plus two HashSet allocations (Methods, ReplayHeaderAllowlist), well below request-flow
        // noise.
        var cloned = appOptions.Clone();
        metadata.Configure(cloned);
        return cloned;
    }

    private string _BuildCacheKey(HttpContext context, IdempotencyOptions options, string keyHeader)
    {
        if (options.KeyDeriver != null)
        {
            return options.KeyDeriver(context, keyHeader);
        }

        var tenant = currentTenant.Id;
        var user = (string?)currentUser.UserId;

        // Refuse to apply idempotency when both tenant and user identity are absent. Returning
        // an empty sentinel signals the caller to fall through to next(). The previous default
        // (`idem::POST:/path:key`) collapsed all anonymous requests sharing the same key path
        // into a single slot, leaking cached responses across unrelated callers.
        if (string.IsNullOrEmpty(tenant) && string.IsNullOrEmpty(user))
        {
            return string.Empty;
        }

        var method = context.Request.Method.ToUpperInvariant();
        var path = context.Request.Path.Value ?? "";
        return $"idem:{tenant ?? string.Empty}:{user ?? "anon"}:{method}:{path}:{keyHeader}";
    }

    /// <summary>
    /// Validates an idempotency-key header value: single-valued, length &lt;= 255, no control
    /// characters (ASCII 0–31 or DEL). Stripe and other vendors enforce these bounds; relaxing
    /// them invites cache-key pollution or DoS.
    /// </summary>
    private static bool _ValidateKeyHeader(StringValues headerValues, string keyHeader, out string reason)
    {
        if (headerValues.Count > 1)
        {
            reason = "multi-valued";
            return false;
        }

        if (keyHeader.Length > 255)
        {
            reason = "length-exceeds-255";
            return false;
        }

        foreach (var c in keyHeader)
        {
            if (c is <= (char)31 or (char)127)
            {
                reason = "control-character";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool _FingerprintEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static async ValueTask<(byte[]? Fingerprint, bool Oversize)> _ComputeFingerprintAsync(
        HttpContext context,
        IdempotencyOptions options,
        CancellationToken ct
    )
    {
        var requestBody = context.Request.Body;
        var cap = options.MaxBodySizeForHashing;
        var useCustomFingerprint = options.RequestFingerprint != null;
        var buffer = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            // When a custom fingerprint delegate is configured we still walk the body to enforce
            // the cap (oversize PassThrough/Reject), but we skip the SHA-256 hashing work since
            // the delegate is the source of truth.
            using var hash = useCustomFingerprint ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var totalRead = 0;

            while (totalRead <= cap)
            {
                var toRead = Math.Min(buffer.Length, cap + 1 - totalRead);
                var read = await requestBody.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;

                if (totalRead > cap)
                {
                    requestBody.Position = 0;
                    return (null, true);
                }

                hash?.AppendData(buffer, 0, read);
            }

            requestBody.Position = 0;

            if (useCustomFingerprint)
            {
                var customFingerprint = await options.RequestFingerprint!(context).ConfigureAwait(false);
                // Rewind unconditionally — the delegate may have consumed the body
                requestBody.Position = 0;
                return (customFingerprint, false);
            }

            return (hash!.GetCurrentHash(), false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency replay hit for key {CacheKey}")]
    private partial void LogReplayHit(string cacheKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency fingerprint mismatch for key {CacheKey}")]
    private partial void LogFingerprintMismatch(string cacheKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency in-flight reject for key {CacheKey}")]
    private partial void LogInFlightReject(string cacheKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency in-flight timeout for key {CacheKey}")]
    private partial void LogInFlightTimeout(string cacheKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency request body exceeded cap {Cap}; rejecting with 413")]
    private partial void LogBodyTooLarge(int cap);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency request body exceeded cap {Cap}; passing through without idempotency guarantees")]
    private partial void LogBodyCapPassThrough(int cap);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency-Key header rejected: {Reason}")]
    private partial void LogKeyMalformed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency skipped: neither tenant nor user identity is present and no KeyDeriver is configured")]
    private partial void LogSkippedNoIdentity();

    [LoggerMessage(Level = LogLevel.Error, Message = "Idempotency finalize (UpsertAsync) failed for key {CacheKey}")]
    private partial void LogFinalizeFailed(string cacheKey, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency finalize skipped: marker for key {CacheKey} no longer owned by this request")]
    private partial void LogFinalizeSkippedMarkerChanged(string cacheKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency marker cleanup failed for key {CacheKey}")]
    private partial void LogMarkerCleanupFailed(string cacheKey, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency cache call failed at {Site} for key {CacheKey}; behavior={Behavior}")]
    private partial void LogCacheFailure(string site, string cacheKey, string behavior, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency lock-provider call failed at {Site} for key {CacheKey}; behavior={Behavior}")]
    private partial void LogLockProviderFailure(string site, string cacheKey, string behavior, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Idempotency winner-lock contended for key {CacheKey}; deferring to in-flight response path")]
    private partial void LogWinnerLockContended(string cacheKey);
}
