// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Idempotency;

/// <summary>
/// Configures the behavior of the idempotency middleware: key derivation, retention, in-flight
/// concurrency strategy and lease, body-fingerprinting limits, response header allowlisting, and
/// store-error handling. All options can be set globally via <c>AddIdempotency()</c> and
/// overridden per endpoint via <c>WithIdempotency()</c>.
/// </summary>
[PublicAPI]
public sealed class IdempotencyOptions
{
    /// <summary>
    /// How long a completed response replays, and how long a released key's record is kept, in the durable store.
    /// Defaults to 24 hours.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

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
    /// How long a request waits for the running attempt when <see cref="InFlightStrategy"/> is
    /// <see cref="InFlightStrategy.WaitAndReplay"/>, before it gives up with 409
    /// <c>g:idempotency_in_flight_timeout</c>. Defaults to 30 seconds. Capped at 1 minute by validation: each waiting
    /// request holds its connection and polls the store for this long, so high concurrency with a long wait
    /// multiplies both.
    /// </summary>
    public TimeSpan InFlightLockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the admitted attempt owns its key unless renewed. The middleware renews the lease every third of this
    /// duration while the handler runs, so it only bounds how long a crashed attempt blocks its key before the next
    /// request takes it over. Defaults to 1 minute; must be between 1 second and 1 hour.
    /// </summary>
    public TimeSpan InFlightLease { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum body size in bytes eligible for fingerprinting. Defaults to 1 MiB. Capped at 64 MiB.</summary>
    public int MaxBodySizeForHashing { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// Number of request-body bytes retained in memory before buffering spills to a temporary
    /// file. Defaults to 1 MiB + 1 byte. This controls memory use only; <see cref="MaxBodySizeForHashing"/>
    /// remains the independent fingerprinting limit.
    /// </summary>
    public int RequestBodyBufferThreshold { get; set; } = (1 * 1024 * 1024) + 1;

    /// <summary>How requests whose body exceeds <see cref="MaxBodySizeForHashing"/> are handled.</summary>
    public OversizeBehavior OversizeBehavior { get; set; } = OversizeBehavior.Reject;

    /// <summary>
    /// How the middleware reacts when the durable idempotency store throws before the handler runs or while a request
    /// waits on another attempt. Defaults to <see cref="OnStoreErrorBehavior.Throw"/>: a store that fails open
    /// silently drops the guarantee it exists for. A completion or release failure after the response started is
    /// always logged, never thrown.
    /// </summary>
    public OnStoreErrorBehavior OnStoreError { get; set; } = OnStoreErrorBehavior.Throw;

    /// <summary>
    /// Whether the default <see cref="KeyDeriver"/> requires an authenticated user identity in
    /// addition to a tenant. Defaults to <see langword="true"/>. When <see langword="true"/>,
    /// requests without a resolved <c>ICurrentUser.UserId</c> are passed through without
    /// idempotency — preventing two anonymous callers in the same tenant from cross-replaying
    /// each other's responses on a shared idempotency key.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> for endpoints that legitimately accept anonymous traffic
    /// at the tenant level (webhook receivers, OAuth callbacks). The key scope falls back
    /// to <c>idem::{method}:{path}{?query}:{key}</c> within the tenant — two anonymous callers in
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
    /// Response headers copied into the stored response at capture time (and replayed verbatim).
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
    /// Determines whether a completed response is stored for replay. A rejected response releases the key, so an
    /// immediate retry runs the handler again.
    /// When <see langword="null"/>, the built-in predicate is used (2xx and selected 4xx; never 5xx, 1xx, 3xx, or transient 4xx).
    /// </summary>
    public Func<HttpContext, bool>? ShouldCacheResponse { get; set; }

    /// <summary>
    /// Determines whether idempotency processing applies to the current request.
    /// When <see langword="null"/>, all requests matching <see cref="Methods"/> that carry the header are processed.
    /// </summary>
    public Func<HttpContext, bool>? ShouldApply { get; set; }

    /// <summary>
    /// Derives the key scope from the <see cref="HttpContext"/> and the raw idempotency key header value.
    /// When <see langword="null"/>, the default <c>idem:{userId}:{method}:{path}{?query}:{key}</c>
    /// derivation is used (query string is included so endpoints that branch on query parameters
    /// don't cross-replay when the same key is reused across sub-modes). Returning an empty string
    /// skips idempotency for the request.
    /// </summary>
    /// <remarks>
    /// The durable store scopes every key by the current tenant, so the scope does not need to carry it. The store key
    /// is the SHA-256 hex of the scope, which keeps long paths and header values within the store's key limit. The
    /// default derivation is unsafe for fully anonymous routes (no tenant, no authenticated user): if both identifiers
    /// are missing, the middleware refuses to apply idempotency and passes the request through. For anonymous or
    /// single-tenant endpoints, configure <see cref="KeyDeriver"/> explicitly so the scope is unambiguous.
    /// </remarks>
    public Func<HttpContext, string, string>? KeyDeriver { get; set; }

    /// <summary>
    /// Computes the request fingerprint (hash) from the buffered body.
    /// When <see langword="null"/>, SHA-256 of the buffered body is used. The durable store records the SHA-256 of
    /// whichever bytes this produces, so a delegate may return a digest of any length.
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
            Retention = Retention,
            HeaderName = HeaderName,
            Methods = new HashSet<string>(Methods, StringComparer.OrdinalIgnoreCase),
            InFlightStrategy = InFlightStrategy,
            InFlightLockTimeout = InFlightLockTimeout,
            InFlightLease = InFlightLease,
            MaxBodySizeForHashing = MaxBodySizeForHashing,
            RequestBodyBufferThreshold = RequestBodyBufferThreshold,
            OversizeBehavior = OversizeBehavior,
            OnStoreError = OnStoreError,
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
    private const int _MaxRequestBodyBufferThreshold = _MaxBodySizeForHashingCap + 1;

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
        RuleFor(x => x.Retention).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MaxBodySizeForHashing)
            .GreaterThan(0)
            .LessThanOrEqualTo(_MaxBodySizeForHashingCap)
            .WithMessage($"MaxBodySizeForHashing must be <= {_MaxBodySizeForHashingCap} bytes (64 MiB).");
        RuleFor(x => x.RequestBodyBufferThreshold)
            .GreaterThan(0)
            .LessThanOrEqualTo(_MaxRequestBodyBufferThreshold)
            .WithMessage(
                $"RequestBodyBufferThreshold must be <= {_MaxRequestBodyBufferThreshold} bytes (64 MiB + 1 byte)."
            );
        RuleFor(x => x.InFlightLockTimeout).GreaterThan(TimeSpan.Zero);
        // The lower bound matches the default idempotency lease minimum and keeps the renewal interval (a third of the
        // lease) from hammering the store; the upper bound caps how long a crashed attempt can block its key.
        RuleFor(x => x.InFlightLease)
            .GreaterThanOrEqualTo(TimeSpan.FromSeconds(1))
            .LessThanOrEqualTo(TimeSpan.FromHours(1))
            .WithMessage("InFlightLease must be between 1 second and 1 hour.");
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
        RuleFor(x => x.OnStoreError).IsInEnum();
        When(
            x => x.InFlightStrategy == InFlightStrategy.WaitAndReplay,
            () =>
            {
                // Cap at 1 minute: each waiting request holds its connection and polls the store for up to this
                // duration. Operators with legitimate long-handler workloads should prefer Reject + client-side
                // backoff (the pattern used by Stripe, AWS, Square, PayPal).
                RuleFor(x => x.InFlightLockTimeout).LessThanOrEqualTo(TimeSpan.FromMinutes(1));
            }
        );
    }
}
