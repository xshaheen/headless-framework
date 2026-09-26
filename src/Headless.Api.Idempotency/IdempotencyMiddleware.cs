// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Cryptography;
using Headless.Abstractions;
using Headless.Api.Idempotency.Resources;
using Headless.Constants;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Headless.Api.Idempotency;

/// <summary>
/// HTTP adapter over durable idempotent admission: admits the request's key, runs the handler behind a renewed
/// fenced lease, and completes the admission with the captured response (or releases it), so a retry replays the
/// stored response from any node.
/// </summary>
internal sealed partial class IdempotencyMiddleware(
    IOptionsMonitor<IdempotencyOptions> optionsMonitor,
    IIdempotentOperations operations,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IProblemDetailsCreator problemDetailsCreator,
    TimeProvider timeProvider,
    ICancellationTokenProvider cancellationTokenProvider,
    ILogger<IdempotencyMiddleware> logger
) : IMiddleware
{
    private static readonly TimeSpan _InitialPollDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _MaxPollDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Processes the incoming request through the idempotency pipeline: key validation, fingerprinting, admission,
    /// in-flight handling, handler execution under a renewed lease, and response capture and completion.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="next">The next middleware delegate.</param>
    /// <exception cref="Exception">
    /// Re-throws any exception from the downstream handler after releasing the admission. Release failures are
    /// logged and swallowed so the original exception propagates cleanly. Store failures before the handler runs
    /// propagate when <see cref="IdempotencyOptions.OnStoreError"/> is <see cref="OnStoreErrorBehavior.Throw"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="IdempotencyOptions.RequestFingerprint"/> returns <see langword="null"/> or an empty
    /// array. Also propagated from the replay path when the response has already started before it is reached
    /// (indicates incorrect middleware ordering).
    /// </exception>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var ct = cancellationTokenProvider.Token;
        // IOptionsMonitor.CurrentValue is a cached singleton read that still observes reloads, avoiding the
        // per-request options rebuild + FluentValidation pass that IOptionsSnapshot.Value forces per scope.
        var appOptions = optionsMonitor.CurrentValue;

        // Read the idempotency-key header using the app-level HeaderName *before* resolving
        // endpoint metadata. HeaderName overrides via WithIdempotency are deliberately ignored —
        // see EndpointConventionBuilderExtensions remarks.
        var headerValues = context.Request.Headers[appOptions.HeaderName];

        // Missing or whitespace key → pass-through
        if (headerValues.Count == 0)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Indexer instead of LastOrDefault(): Count != 0 was checked above, and the LINQ path boxes StringValues.
        var keyHeader = headerValues[^1];
        if (string.IsNullOrWhiteSpace(keyHeader))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Now resolve per-endpoint options for everything else.
        var options = _ResolveOptions(context, appOptions);

        // Method not opted-in → pass-through
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

        // Buffer body so it can be read multiple times. The memory-to-disk threshold is independent
        // from the hashing cap, and this still does NOT impose a hard buffer limit — PassThrough
        // mode requires downstream handlers to read bodies that legitimately exceed the cap.
        context.Request.EnableBuffering(options.RequestBodyBufferThreshold);

        var (requestHash, oversize) = await _ComputeRequestHashAsync(context, options, ct).ConfigureAwait(false);

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
                problemDetailsCreator.Normalize(pd);
                await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
                return;
            }

            // PassThrough: log and let the request through without idempotency guarantees
            LogBodyCapPassThrough(options.MaxBodySizeForHashing);
            await next(context).ConfigureAwait(false);
            return;
        }

        // Derive the key scope. The default derivation requires a tenant or authenticated user;
        // for fully anonymous routes with no KeyDeriver override, refuse to apply idempotency
        // rather than let unrelated callers share a key.
        var scope = _BuildScope(context, options, keyHeader);
        if (scope.Length == 0)
        {
            LogSkippedNoIdentity();
            await next(context).ConfigureAwait(false);
            return;
        }

        var request = new AdmissionRequest(
            keyHeader,
            scope,
            HashScope(scope),
            IdempotencyFingerprint.Compute(requestHash!)
        );

        IdempotentAdmission admission;
        try
        {
            admission = await _AdmitAsync(request, options, ct).ConfigureAwait(false);
        }
        catch (Exception storeEx) when (_IsStoreFailure(storeEx, ct))
        {
            LogStoreFailure("admit", request.Key, options.OnStoreError.ToString(), storeEx);
            if (options.OnStoreError == OnStoreErrorBehavior.Throw)
            {
                throw;
            }

            await next(context).ConfigureAwait(false);
            return;
        }

        switch (admission.Disposition)
        {
            case IdempotentDisposition.InFlight when options.InFlightStrategy == InFlightStrategy.WaitAndReplay:
                await _WaitAndReplayAsync(context, next, request, admission, options, ct).ConfigureAwait(false);
                return;
            case IdempotentDisposition.InFlight:
                LogInFlightReject(request.Key);
                var pd = problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlight());
                await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
                return;
            default:
                await _DispatchSettledAsync(context, next, admission, request, options, ct).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>
    /// Returns the store key for a scope string: the lowercase SHA-256 hex of its UTF-8 bytes. Always 64 characters,
    /// so a long path or a 255-character header key still fits the store's key limit, and the raw scope (which carries
    /// the user id) never reaches the store or the logs.
    /// </summary>
    internal static string HashScope(string scope)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
    }

    private ValueTask<IdempotentAdmission> _AdmitAsync(
        AdmissionRequest request,
        IdempotencyOptions options,
        CancellationToken ct
    )
    {
        return operations.AdmitAsync(
            request.Key,
            request.Fingerprint,
            IdempotencyResponseSnapshot.Contract,
            options.InFlightLease,
            options.Retention,
            ct
        );
    }

    /// <summary>Routes an admission that is not in flight: run the handler, replay, or report the conflict.</summary>
    private async Task _DispatchSettledAsync(
        HttpContext context,
        RequestDelegate next,
        IdempotentAdmission admission,
        AdmissionRequest request,
        IdempotencyOptions options,
        CancellationToken ct
    )
    {
        switch (admission.Disposition)
        {
            case IdempotentDisposition.Admitted:
                await _ExecuteAndCompleteAsync(context, next, admission, request, options).ConfigureAwait(false);
                return;
            case IdempotentDisposition.Replay:
                await _ReplayAsync(context, admission.Result!, options, request.Key, ct).ConfigureAwait(false);
                return;
            default:
                if (admission.StoredContract is not null)
                {
                    // Same request, but the stored response was written under another snapshot contract (a version
                    // change); it cannot be replayed and must not be re-run, so it is reported like a reused key.
                    LogContractMismatch(request.Key, admission.StoredContract);
                }
                else
                {
                    LogFingerprintMismatch(request.Key);
                }

                await _WriteMismatchAsync(context, options).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>
    /// Polls with a doubling, capped backoff until the running attempt settles or the wait budget ends. Each tick
    /// reads the cheap, lock-free <see cref="IIdempotentOperations.PeekAsync" /> instead of the full row-locking
    /// admission; the full admission — the only call that can actually replay a result or take the key over — runs
    /// only when the peek reports the record is no longer <see cref="IdempotencyPeekStatus.Pending" />, or when the
    /// current holder's lease may already have expired, so a stalled attempt is taken over without waiting out the
    /// rest of the poll budget on reads that could only ever confirm the same stale "in flight" status.
    /// </summary>
    private async Task _WaitAndReplayAsync(
        HttpContext context,
        RequestDelegate next,
        AdmissionRequest request,
        IdempotentAdmission admission,
        IdempotencyOptions options,
        CancellationToken ct
    )
    {
        var startedAt = timeProvider.GetTimestamp();
        var delay = _InitialPollDelay;
        var holderLeaseExpiresAt = admission.LeaseExpiresAt;

        while (true)
        {
            var remaining = options.InFlightLockTimeout - timeProvider.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(delay < remaining ? delay : remaining, timeProvider, ct).ConfigureAwait(false);
            delay = delay * 2 < _MaxPollDelay ? delay * 2 : _MaxPollDelay;

            // Once the holder's last-known lease expiry has passed, a peek can only ever repeat the stale "in
            // flight" status it read before that instant: skip straight to the full admission, which alone can
            // renew, take over, or replay.
            var leaseMayHaveExpired = holderLeaseExpiresAt is { } expiry && timeProvider.GetUtcNow() >= expiry;

            if (!leaseMayHaveExpired)
            {
                IdempotencyPeekStatus peek;

                try
                {
                    peek = await operations.PeekAsync(request.Key, ct).ConfigureAwait(false);
                }
                catch (Exception storeEx) when (_IsStoreFailure(storeEx, ct))
                {
                    LogStoreFailure("wait-peek", request.Key, options.OnStoreError.ToString(), storeEx);
                    if (options.OnStoreError == OnStoreErrorBehavior.Throw)
                    {
                        throw;
                    }

                    // Another attempt holds the key, so running the handler here could execute the operation twice.
                    // A recoverable 409 lets the client retry once the store is back.
                    break;
                }

                if (peek == IdempotencyPeekStatus.Pending)
                {
                    // Nothing changed since the last admission: another tick of the cheap read is worth far less
                    // than a row-locking write that would only confirm the same disposition.
                    continue;
                }
            }

            IdempotentAdmission reAdmission;

            try
            {
                reAdmission = await _AdmitAsync(request, options, ct).ConfigureAwait(false);
            }
            catch (Exception storeEx) when (_IsStoreFailure(storeEx, ct))
            {
                LogStoreFailure("wait-admit", request.Key, options.OnStoreError.ToString(), storeEx);
                if (options.OnStoreError == OnStoreErrorBehavior.Throw)
                {
                    throw;
                }

                // Another attempt holds the key, so running the handler here could execute the operation twice.
                // A recoverable 409 lets the client retry once the store is back.
                break;
            }

            if (reAdmission.Disposition != IdempotentDisposition.InFlight)
            {
                await _DispatchSettledAsync(context, next, reAdmission, request, options, ct).ConfigureAwait(false);
                return;
            }

            holderLeaseExpiresAt = reAdmission.LeaseExpiresAt;
        }

        LogInFlightTimeout(request.Key);
        var pd = problemDetailsCreator.Conflict(IdempotencyMessageDescriber.InFlightTimeout());
        await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _ExecuteAndCompleteAsync(
        HttpContext context,
        RequestDelegate next,
        IdempotentAdmission admission,
        AdmissionRequest request,
        IdempotencyOptions options
    )
    {
        if (admission.IsTakeover)
        {
            LogTakeover(request.Key);
        }

        // Set synchronously before the handler runs: an HttpContext feature reaches every downstream component,
        // whereas ambient AsyncLocal state set in an async method would not flow back out of it.
        context.Features.Set<IIdempotencyContext>(
            new IdempotencyContext(request.HeaderKey, request.Scope, request.Key, admission)
        );

        var originalBody = context.Response.Body;
        await using var captureStream = new CaptureStream(originalBody, options.MaxBodySizeForHashing);
        context.Response.Body = captureStream;

        using var renewalStop = new CancellationTokenSource();
        var renewal = _RenewWhileRunningAsync(admission, options.InFlightLease, request.Key, renewalStop.Token);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception handlerEx)
        {
            context.Response.Body = originalBody;
            LogHandlerThrew(request.Key, handlerEx);
            await renewalStop.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
            await _ReleaseAsync(context, admission, request.Key, options, rethrowOnFailure: false)
                .ConfigureAwait(false);
            throw;
        }

        context.Response.Body = originalBody;

        // Stop renewing before settling, so a renewal cannot race the completion that ends the lease.
        // The loop catches its own failures, so awaiting it only waits for the in-flight renewal to finish.
        await renewalStop.CancelAsync().ConfigureAwait(false);
        await renewal.ConfigureAwait(false);

        var shouldStore =
            (options.ShouldCacheResponse ?? DefaultCachePredicate.Instance)(context) && !captureStream.TruncatedCapture;

        if (!shouldStore)
        {
            await _ReleaseAsync(context, admission, request.Key, options, rethrowOnFailure: true).ConfigureAwait(false);
            return;
        }

        var capturedHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in context.Response.Headers)
        {
            if (options.ReplayHeaderAllowlist.Contains(header.Key))
            {
                capturedHeaders[header.Key] = header.Value.ToArray()!;
            }
        }

        var snapshot = new IdempotencyResponseSnapshot
        {
            StatusCode = context.Response.StatusCode,
            Headers = capturedHeaders,
            Body = captureStream.CapturedBytes,
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            IdempotencyJsonContext.Default.IdempotencyResponseSnapshot
        );

        try
        {
            // CancellationToken.None: the handler already ran, so a client disconnect must not strand the admission
            // pending until its lease expires, which would turn every retry into a 409 for that long.
            await operations
                .CompleteAsync(
                    admission,
                    payload,
                    IdempotencyResponseSnapshot.Contract,
                    cancellationToken: CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        catch (StaleLeaseException staleEx)
        {
            // The lease expired or was taken over while the handler ran; the fence refused this result so only the
            // owning attempt's response is stored. Nothing the client can act on, so it is logged, never thrown.
            LogCompletionRefused(request.Key, staleEx);
        }
        catch (Exception storeEx)
        {
            LogCompletionFailed(request.Key, storeEx);
            if (_ShouldRethrowAfterHandler(context, options))
            {
                throw;
            }
        }
    }

    private async Task _ReleaseAsync(
        HttpContext context,
        IdempotentAdmission admission,
        string key,
        IdempotencyOptions options,
        bool rethrowOnFailure
    )
    {
        try
        {
            var status = await operations.ReleaseAsync(admission, CancellationToken.None).ConfigureAwait(false);
            if (status != LeaseSettlementStatus.Released)
            {
                LogReleaseRefused(key, status);
            }
        }
        catch (Exception storeEx)
        {
            LogReleaseFailed(key, storeEx);
            if (rethrowOnFailure && _ShouldRethrowAfterHandler(context, options))
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Whether a store failure after the handler ran should propagate: only while the response has not started and the
    /// options ask for it. Once the response started the client already has it, and throwing would only log a 500 it
    /// never sees.
    /// </summary>
    private static bool _ShouldRethrowAfterHandler(HttpContext context, IdempotencyOptions options)
    {
        return !context.Response.HasStarted && options.OnStoreError == OnStoreErrorBehavior.Throw;
    }

    /// <summary>
    /// Renews the admitted lease every third of its duration until stopped or the lease is lost. Each renewal is
    /// bounded by the same interval: a handler that holds its fenced transaction blocks the renewal on the lease row,
    /// and an unbounded call would stall the loop until that transaction ends.
    /// </summary>
    private async Task _RenewWhileRunningAsync(
        IdempotentAdmission admission,
        TimeSpan lease,
        string key,
        CancellationToken stopToken
    )
    {
        var interval = lease / 3;

        try
        {
            while (true)
            {
                await Task.Delay(interval, timeProvider, stopToken).ConfigureAwait(false);

                using var callCts = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
                var renewTask = operations.RenewAsync(admission, lease, callCts.Token).AsTask();
                LeaseRenewalResult result;

                try
                {
                    result = await renewTask.WaitAsync(interval, timeProvider, stopToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Cancel the abandoned call so it releases its connection, and observe its outcome so a late
                    // failure is not reported as an unobserved task exception.
                    await callCts.CancelAsync().ConfigureAwait(false);
                    _ = renewTask.ContinueWith(
                        static t => _ = t.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default
                    );
                    LogRenewalTimedOut(key, interval);
                    continue;
                }
                catch (Exception renewEx) when (!stopToken.IsCancellationRequested)
                {
                    LogRenewalFailed(key, renewEx);
                    continue;
                }

                if (!result.IsRenewed)
                {
                    // The lease expired, was taken over, or ended; renewing further cannot win it back, and the
                    // completion's fence will refuse this attempt's result.
                    LogLeaseLost(key, result.Status);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // The handler finished; the loop's job is done.
        }
    }

    private static bool _IsStoreFailure(Exception exception, CancellationToken ct)
    {
        // A cancellation the request itself asked for is not a store failure; let it propagate.
        return exception is not OperationCanceledException || !ct.IsCancellationRequested;
    }

    /// <summary>
    /// Writes the stored response to the current response, replaying status code, allowlisted headers, and body
    /// exactly as originally captured. Sets the <c>Idempotent-Replayed: true</c> response header.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The response has already started. Indicates <c>UseIdempotency()</c> is ordered after
    /// middleware that already wrote to the response body.
    /// </exception>
    private async Task _ReplayAsync(
        HttpContext context,
        IdempotentResult result,
        IdempotencyOptions options,
        string key,
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

        var snapshot =
            result.Deserialize(IdempotencyJsonContext.Default.IdempotencyResponseSnapshot)
            ?? throw new InvalidOperationException("The stored idempotent response is empty.");

        context.Response.StatusCode = snapshot.StatusCode;

        // Strip pre-existing allowlisted headers set by upstream middleware so byte-equivalent
        // replay isn't poisoned by per-request mutations (CORS, security policies). Headers
        // outside the allowlist (e.g., traceparent from logging) remain untouched.
        foreach (var allowedHeader in options.ReplayHeaderAllowlist)
        {
            context.Response.Headers.Remove(allowedHeader);
        }

        foreach (var (name, values) in snapshot.Headers)
        {
            if (options.ReplayHeaderAllowlist.Contains(name))
            {
                context.Response.Headers[name] = values;
            }
        }

        context.Response.Headers[HttpHeaderNames.IdempotentReplayed] = "true";

        if (snapshot.Body.Length > 0)
        {
            context.Response.ContentLength = snapshot.Body.Length;
            await context.Response.Body.WriteAsync(snapshot.Body, ct).ConfigureAwait(false);
        }

        LogReplayHit(key);
    }

    private async Task _WriteMismatchAsync(HttpContext context, IdempotencyOptions options)
    {
        var descriptor = IdempotencyMessageDescriber.KeyReused();

        var pd =
            options.MismatchStatusCode == StatusCodes.Status409Conflict
                ? problemDetailsCreator.Conflict(descriptor)
                : problemDetailsCreator.UnprocessableEntity(
                    new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal)
                    {
                        ["idempotency_key"] = [descriptor],
                    }
                );

        await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private async Task _WriteBadRequestAsync(HttpContext context, ErrorDescriptor descriptor)
    {
        var pd = problemDetailsCreator.BadRequest(detail: descriptor.Description, error: descriptor);
        await Results.Problem(pd).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static IdempotencyOptions _ResolveOptions(HttpContext context, IdempotencyOptions appOptions)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>();
        if (metadata is null)
        {
            return appOptions;
        }

        // Clone + merge per request. A static cache keyed by metadata captured `appOptions` from
        // the first observation, which silently ignored subsequent IOptionsMonitor reloads for
        // endpoints with WithIdempotency(...) while plain endpoints honored reloads — an
        // asymmetric drift that was very hard to debug. Cloning per request costs a struct copy
        // plus two HashSet allocations (Methods, ReplayHeaderAllowlist), well below request-flow
        // noise.
        var cloned = appOptions.Clone();
        metadata.Configure(cloned);
        return cloned;
    }

    private string _BuildScope(HttpContext context, IdempotencyOptions options, string keyHeader)
    {
        if (options.KeyDeriver != null)
        {
            return options.KeyDeriver(context, keyHeader);
        }

        var tenant = currentTenant.Id;
        var user = (string?)currentUser.UserId;

        // Refuse to apply idempotency when neither identity is resolvable. When RequireUserIdentity
        // is true (default), tenant-only requests also fall through — preventing two anonymous
        // callers in the same tenant from cross-replaying each other's responses on a shared
        // Idempotency-Key. Operators with intentional anon-within-tenant flows (webhook receivers,
        // OAuth callbacks) set RequireUserIdentity=false and accept the trade-off, or configure
        // KeyDeriver with a stable per-caller identifier.
        var tenantMissing = string.IsNullOrEmpty(tenant);
        var userMissing = string.IsNullOrEmpty(user);

        if (tenantMissing && userMissing)
        {
            return string.Empty;
        }

        if (options.RequireUserIdentity && userMissing)
        {
            return string.Empty;
        }

        var method = _CanonicalMethod(context.Request.Method);
        var path = context.Request.Path.Value ?? "";
        // QueryString participates so endpoints that branch on query parameters (e.g.,
        // ?action=void vs ?action=capture, ?dry_run=true vs ?dry_run=false) don't cross-replay
        // when the client reuses the same idempotency key. QueryString.Value includes the
        // leading "?" or is empty when no query exists.
        var query = context.Request.QueryString.Value ?? string.Empty;
        // The tenant is not part of the scope: the durable store keys every record by the current
        // tenant. Anonymous-user fallback uses an empty segment rather than a literal "anon" so a
        // real UserId equal to the string "anon" cannot collide with the anonymous bucket.
        return $"idem:{user ?? string.Empty}:{method}:{path}{query}:{keyHeader}";
    }

    /// <summary>
    /// Returns the canonical (uppercase) form of an HTTP method without per-request allocation
    /// for the common cases. <c>HttpMethods.IsXxx</c> performs an ordinal-ignore-case comparison
    /// and the corresponding constant is an interned string, so a request arriving with any
    /// casing of a known method maps to the same constant reference.
    /// </summary>
    private static string _CanonicalMethod(string method)
    {
        return method switch
        {
            _ when HttpMethods.IsPost(method) => HttpMethods.Post,
            _ when HttpMethods.IsPut(method) => HttpMethods.Put,
            _ when HttpMethods.IsPatch(method) => HttpMethods.Patch,
            _ when HttpMethods.IsDelete(method) => HttpMethods.Delete,
            _ when HttpMethods.IsGet(method) => HttpMethods.Get,
            _ when HttpMethods.IsHead(method) => HttpMethods.Head,
            _ when HttpMethods.IsOptions(method) => HttpMethods.Options,
            _ when HttpMethods.IsTrace(method) => HttpMethods.Trace,
            _ when HttpMethods.IsConnect(method) => HttpMethods.Connect,
            _ => method.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Validates an idempotency-key header value: single-valued, length &lt;= 255, no control
    /// characters (ASCII 0–31 or DEL). Stripe and other vendors enforce these bounds; relaxing
    /// them invites key pollution or DoS.
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

    /// <summary>
    /// Walks the buffered body to enforce the hashing cap and returns the bytes the fingerprint is computed over:
    /// SHA-256 of the body, or the custom <see cref="IdempotencyOptions.RequestFingerprint"/> output.
    /// </summary>
    private static async ValueTask<(byte[]? Hash, bool Oversize)> _ComputeRequestHashAsync(
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

                // A delegate that returns null/empty would collapse every distinct request body
                // onto a single fingerprint, letting any two requests cross-replay. Treat that as
                // a programming error.
                if (customFingerprint is null || customFingerprint.Length == 0)
                {
                    throw new InvalidOperationException(
                        "IdempotencyOptions.RequestFingerprint returned null or an empty byte[]; "
                            + "the delegate must produce a non-empty hash of the request payload."
                    );
                }

                return (customFingerprint, false);
            }

            return (hash!.GetCurrentHash(), false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The per-request identity of an admission: header value, scope, store key, and fingerprint.</summary>
    private sealed record AdmissionRequest(
        string HeaderKey,
        string Scope,
        string Key,
        IdempotencyFingerprint Fingerprint
    );

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency replay hit for key {IdempotencyKey}")]
    // ReSharper disable once InconsistentNaming
    private partial void LogReplayHit(string idempotencyKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency fingerprint mismatch for key {IdempotencyKey}")]
    // ReSharper disable once InconsistentNaming
    private partial void LogFingerprintMismatch(string idempotencyKey);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency response for key {IdempotencyKey} is stored under contract {StoredContract} and cannot be replayed"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogContractMismatch(string idempotencyKey, string storedContract);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency in-flight reject for key {IdempotencyKey}")]
    // ReSharper disable once InconsistentNaming
    private partial void LogInFlightReject(string idempotencyKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency in-flight timeout for key {IdempotencyKey}")]
    // ReSharper disable once InconsistentNaming
    private partial void LogInFlightTimeout(string idempotencyKey);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Idempotency key {IdempotencyKey} taken over from an attempt that ended without completing"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogTakeover(string idempotencyKey);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency request body exceeded cap {Cap}; rejecting with 413"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogBodyTooLarge(int cap);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency request body exceeded cap {Cap}; passing through without idempotency guarantees"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogBodyCapPassThrough(int cap);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency-Key header rejected: {Reason}")]
    // ReSharper disable once InconsistentNaming
    private partial void LogKeyMalformed(string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency skipped: neither tenant nor user identity is present and no KeyDeriver is configured"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogSkippedNoIdentity();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency store call failed at {Site} for key {IdempotencyKey}; behavior={Behavior}"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogStoreFailure(string site, string idempotencyKey, string behavior, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency completion refused for key {IdempotencyKey}: the attempt no longer owns the key"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogCompletionRefused(string idempotencyKey, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Idempotency completion failed for key {IdempotencyKey}")]
    // ReSharper disable once InconsistentNaming
    private partial void LogCompletionFailed(string idempotencyKey, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency release refused for key {IdempotencyKey} with status {Status}"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogReleaseRefused(string idempotencyKey, LeaseSettlementStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency release failed for key {IdempotencyKey}")]
    // ReSharper disable once InconsistentNaming
    private partial void LogReleaseFailed(string idempotencyKey, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency lease renewal for key {IdempotencyKey} did not finish within {Timeout}; retrying on the next tick"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogRenewalTimedOut(string idempotencyKey, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency lease renewal failed for key {IdempotencyKey}")]
    // ReSharper disable once InconsistentNaming
    private partial void LogRenewalFailed(string idempotencyKey, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Idempotency lease for key {IdempotencyKey} lost ({Status}); renewal stopped"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogLeaseLost(string idempotencyKey, LeaseRenewalStatus status);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Idempotency handler threw for key {IdempotencyKey}; releasing the admission before propagating"
    )]
    // ReSharper disable once InconsistentNaming
    private partial void LogHandlerThrew(string idempotencyKey, Exception exception);
}
