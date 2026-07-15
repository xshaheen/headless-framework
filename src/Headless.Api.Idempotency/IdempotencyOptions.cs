// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Headless.DistributedLocks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api.Idempotency;

/// <summary>
/// Configures the behavior of the idempotency middleware: key derivation, TTL, in-flight
/// concurrency strategy, body-fingerprinting limits, response header allowlisting, and
/// cache-error handling. All options can be set globally via <c>AddIdempotency()</c> and
/// overridden per endpoint via <c>WithIdempotency()</c>.
/// </summary>
[PublicAPI]
public sealed class IdempotencyOptions
{
    /// <summary>How long an idempotency record is retained after the first successful response. Defaults to 24 hours.</summary>
    public TimeSpan IdempotencyKeyExpiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Request header that carries the idempotency key. Defaults to <c>Idempotency-Key</c>
    /// (per IETF <c>draft-ietf-httpapi-idempotency-key-header</c>). This value is read at the
    /// start of the middleware pipeline, before endpoint metadata is resolved, so
    /// per-endpoint overrides via <see cref="IdempotencyMetadata.Configure"/> are ignored
    /// for this property.
    /// </summary>
    public string HeaderName { get; set; } = HttpHeaderNames.IdempotencyKey;

    /// <summary>HTTP methods for which idempotency is enforced. GET is never valid.</summary>
    /// <remarks>
    /// Exposed as <see cref="ISet{T}"/> so per-endpoint <see cref="IdempotencyMetadata.Configure"/>
    /// delegates may add or remove individual entries without replacing the whole set. The
    /// middleware clones the set per request before invoking the delegate, so consumer mutation
    /// is safe and does not leak to other requests.
    /// </remarks>
    public ISet<string> Methods { get; set; } =
        new HashSet<string>(["POST", "PUT", "PATCH", "DELETE"], StringComparer.OrdinalIgnoreCase);

    /// <summary>How concurrent in-flight requests with the same key are handled.</summary>
    public InFlightStrategy InFlightStrategy { get; set; } = InFlightStrategy.Reject;

    /// <summary>
    /// How long a loser request blocks waiting for the winner to finalize when
    /// <see cref="InFlightStrategy"/> is <see cref="InFlightStrategy.WaitAndReplay"/>.
    /// Defaults to 30 seconds. Capped at 1 minute by validation: each waiting request holds
    /// an ASP.NET worker thread for this duration, so high concurrency combined with a long
    /// timeout risks thread-pool exhaustion.
    /// </summary>
    public TimeSpan InFlightLockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Lease duration for the winner's distributed lock under
    /// <see cref="InFlightStrategy.WaitAndReplay"/>. Sized to outlive the handler's worst-case
    /// runtime; on lease expiry, the lock is released by the lock provider and another request
    /// for the same key may acquire it, breaking mutual exclusion mid-handler. Defaults to
    /// 5 minutes. Must be greater than or equal to <see cref="InFlightLockTimeout"/> and at most
    /// 1 hour.
    /// </summary>
    /// <remarks>
    /// A long lease means a crashed winner blocks the key for up to this duration; a short lease
    /// risks losing mutual exclusion on long-running handlers. 5 minutes is a conservative default
    /// for typical mutation endpoints. Operators with handlers expected to exceed this should
    /// raise the value or implement <c>RenewAsync</c> heartbeats in their lock provider.
    /// </remarks>
    public TimeSpan WinnerLockLease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum body size in bytes eligible for fingerprinting. Defaults to 1 MiB. Capped at 64 MiB.</summary>
    public int MaxBodySizeForHashing { get; set; } = 1 * 1024 * 1024;

    /// <summary>How requests whose body exceeds <see cref="MaxBodySizeForHashing"/> are handled.</summary>
    public OversizeBehavior OversizeBehavior { get; set; } = OversizeBehavior.Reject;

    /// <summary>
    /// How the middleware reacts when the idempotency backing store throws — either the
    /// underlying <see cref="Headless.Caching.ICache"/> or the
    /// <see cref="Headless.DistributedLocks.IDistributedLock"/> used by
    /// <see cref="InFlightStrategy.WaitAndReplay"/>. Defaults to
    /// <see cref="OnCacheErrorBehavior.FailOpen"/>: log a warning and bypass idempotency for the
    /// failing request. Switch to <see cref="OnCacheErrorBehavior.Throw"/> for environments
    /// that prefer 5xx over silently dropping the guarantee.
    /// </summary>
    public OnCacheErrorBehavior OnCacheError { get; set; } = OnCacheErrorBehavior.FailOpen;

    /// <summary>
    /// Whether the default <see cref="KeyDeriver"/> requires an authenticated user identity in
    /// addition to a tenant. Defaults to <see langword="true"/>. When <see langword="true"/>,
    /// requests without a resolved <c>ICurrentUser.UserId</c> are passed through without
    /// idempotency — preventing two anonymous callers in the same tenant from cross-replaying
    /// each other's responses on a shared idempotency key.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> for endpoints that legitimately accept anonymous traffic
    /// at the tenant level (webhook receivers, OAuth callbacks). The cache namespace falls back
    /// to <c>idem:{tenant}::{method}:{path}{?query}:{key}</c> — two anonymous callers within
    /// the same tenant sharing an Idempotency-Key WILL replay each other's responses. Operators
    /// turning this off should ensure callers within the tenant boundary are mutually trusted
    /// or configure <see cref="KeyDeriver"/> with a stable per-caller identifier.
    /// </remarks>
    public bool RequireUserIdentity { get; set; } = true;

    /// <summary>
    /// HTTP status code returned when the same idempotency key is reused with a different
    /// request body. Must be 409 (Conflict) or 422 (Unprocessable Entity). Defaults to 422.
    /// Use 409 when clients should treat the mismatch as a general conflict; use 422 (default)
    /// when clients should treat it as a semantic validation error on the
    /// <c>idempotency_key</c> field (matches the Stripe and OpenAPI convention).
    /// </summary>
    public int MismatchStatusCode { get; set; } = StatusCodes.Status422UnprocessableEntity;

    /// <summary>
    /// Response headers copied into the cached record at capture time (and replayed verbatim).
    /// Headers not in this set are dropped at capture; <c>Set-Cookie</c> and <c>traceparent</c> are excluded by design.
    /// </summary>
    /// <remarks>
    /// Exposed as <see cref="ISet{T}"/> so per-endpoint overrides can extend or trim the allowlist
    /// in place. The middleware clones the set per request before delegate invocation.
    /// </remarks>
    public ISet<string> ReplayHeaderAllowlist { get; set; } =
        new HashSet<string>(
            [
                "Content-Type",
                "Content-Language",
                "Content-Encoding",
                "Content-Disposition",
                "Location",
                "Link",
                "ETag",
                "Last-Modified",
                "Cache-Control",
                "Vary",
            ],
            StringComparer.OrdinalIgnoreCase
        );

    /// <summary>
    /// Determines whether a completed response should be cached for replay.
    /// When <see langword="null"/>, the built-in predicate is used (2xx and selected 4xx; never 5xx, 1xx, 3xx, or transient 4xx).
    /// </summary>
    public Func<HttpContext, bool>? ShouldCacheResponse { get; set; }

    /// <summary>
    /// Determines whether idempotency processing applies to the current request.
    /// When <see langword="null"/>, all requests matching <see cref="Methods"/> that carry the header are processed.
    /// </summary>
    public Func<HttpContext, bool>? ShouldApply { get; set; }

    /// <summary>
    /// Derives the cache key from the <see cref="HttpContext"/> and the raw idempotency key header value.
    /// When <see langword="null"/>, the default <c>idem:{tenant}:{userId}:{method}:{path}{?query}:{key}</c>
    /// derivation is used (query string is included so endpoints that branch on query parameters
    /// don't cross-replay when the same key is reused across sub-modes).
    /// </summary>
    /// <remarks>
    /// The default derivation is unsafe for fully anonymous routes (no tenant, no authenticated
    /// user): if both identifiers are missing, the middleware refuses to apply idempotency and
    /// passes the request through. For anonymous or single-tenant endpoints, configure
    /// <see cref="KeyDeriver"/> explicitly so the cache namespace is unambiguous.
    /// </remarks>
    public Func<HttpContext, string, string>? KeyDeriver { get; set; }

    /// <summary>
    /// Computes the request fingerprint (hash) from the buffered body.
    /// When <see langword="null"/>, SHA-256 of the buffered body is used.
    /// The delegate receives a buffered, zero-positioned request stream.
    /// </summary>
    public Func<HttpContext, ValueTask<byte[]>>? RequestFingerprint { get; set; }

    /// <summary>
    /// Returns a deep copy of this options instance with fresh mutable collections,
    /// so per-endpoint delegates can mutate <see cref="Methods"/> and
    /// <see cref="ReplayHeaderAllowlist"/> without affecting the application-level options.
    /// </summary>
    [Pure]
    internal IdempotencyOptions Clone()
    {
        return new()
        {
            IdempotencyKeyExpiration = IdempotencyKeyExpiration,
            HeaderName = HeaderName,
            Methods = new HashSet<string>(Methods, StringComparer.OrdinalIgnoreCase),
            InFlightStrategy = InFlightStrategy,
            InFlightLockTimeout = InFlightLockTimeout,
            WinnerLockLease = WinnerLockLease,
            MaxBodySizeForHashing = MaxBodySizeForHashing,
            OversizeBehavior = OversizeBehavior,
            OnCacheError = OnCacheError,
            RequireUserIdentity = RequireUserIdentity,
            MismatchStatusCode = MismatchStatusCode,
            ReplayHeaderAllowlist = new HashSet<string>(ReplayHeaderAllowlist, StringComparer.OrdinalIgnoreCase),
            ShouldCacheResponse = ShouldCacheResponse,
            ShouldApply = ShouldApply,
            KeyDeriver = KeyDeriver,
            RequestFingerprint = RequestFingerprint,
        };
    }
}

internal sealed class IdempotencyOptionsValidator : AbstractValidator<IdempotencyOptions>
{
    private const int _MaxBodySizeForHashingCap = 64 * 1024 * 1024; // 64 MiB

    private static readonly HashSet<string> _ValidMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "HEAD",
        "OPTIONS",
        "CONNECT",
        "TRACE",
    };

    public IdempotencyOptionsValidator()
    {
        RuleFor(x => x.IdempotencyKeyExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MaxBodySizeForHashing)
            .GreaterThan(0)
            .LessThanOrEqualTo(_MaxBodySizeForHashingCap)
            .WithMessage($"MaxBodySizeForHashing must be <= {_MaxBodySizeForHashingCap} bytes (64 MiB).");
        RuleFor(x => x.InFlightLockTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.HeaderName).NotEmpty();
        RuleFor(x => x.Methods).NotEmpty();
        RuleForEach(x => x.Methods)
            .Must(m => !HttpMethods.IsGet(m))
            .WithMessage("GET is not a valid idempotency method.")
            .Must(m => _ValidMethods.Contains(m))
            .WithMessage("'{PropertyValue}' is not a recognized HTTP method.");
        RuleFor(x => x.ReplayHeaderAllowlist).NotNull();
        RuleFor(x => x.MismatchStatusCode)
            .Must(c => c is StatusCodes.Status409Conflict or StatusCodes.Status422UnprocessableEntity)
            .WithMessage("MismatchStatusCode must be 409 or 422.");
        RuleFor(x => x.OnCacheError).IsInEnum();
        When(
            x => x.InFlightStrategy == InFlightStrategy.WaitAndReplay,
            () =>
            {
                // Cap at 1 minute: each loser holds an ASP.NET worker thread for up to this duration.
                // High retry concurrency × long timeout → thread-pool exhaustion. Operators with
                // legitimate long-handler workloads should prefer Reject + client-side backoff
                // (the pattern used by Stripe, AWS, Square, PayPal).
                RuleFor(x => x.InFlightLockTimeout).LessThanOrEqualTo(TimeSpan.FromMinutes(1));
                RuleFor(x => x.WinnerLockLease)
                    .GreaterThan(TimeSpan.Zero)
                    .LessThanOrEqualTo(TimeSpan.FromHours(1))
                    .WithMessage("WinnerLockLease must be <= 1 hour.");
                RuleFor(x => x.WinnerLockLease)
                    .GreaterThanOrEqualTo(x => x.InFlightLockTimeout)
                    .WithMessage(
                        "WinnerLockLease must be >= InFlightLockTimeout (otherwise the lock can expire before the loser's acquire deadline)."
                    );
            }
        );
    }
}

/// <summary>
/// DI-aware validator that fails fast at host startup when
/// <see cref="IdempotencyOptions.InFlightStrategy"/> is
/// <see cref="InFlightStrategy.WaitAndReplay"/> but no
/// <see cref="IDistributedLock"/> is registered.
/// </summary>
internal sealed class IdempotencyOptionsDiValidator(IServiceProvider serviceProvider)
    : IValidateOptions<IdempotencyOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyOptions options)
    {
        if (options.InFlightStrategy != InFlightStrategy.WaitAndReplay)
        {
            return ValidateOptionsResult.Success;
        }

        var lockProvider = serviceProvider.GetService<IDistributedLock>();

        if (lockProvider is not null)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{nameof(IdempotencyOptions)}.{nameof(IdempotencyOptions.InFlightStrategy)} = "
                + $"{nameof(InFlightStrategy.WaitAndReplay)} requires {nameof(IDistributedLock)} "
                + "to be registered. Either switch InFlightStrategy to Reject or register a distributed-lock provider."
        );
    }
}
