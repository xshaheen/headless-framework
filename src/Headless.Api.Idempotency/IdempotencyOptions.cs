// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Headless.DistributedLocks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api.Idempotency;

[PublicAPI]
public sealed class IdempotencyOptions
{
    /// <summary>How long an idempotency record is retained after the first successful response. Defaults to 24 hours.</summary>
    public TimeSpan IdempotencyKeyExpiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Request header that carries the idempotency key. Defaults to <c>X-Idempotency-Key</c>.</summary>
    public string HeaderName { get; set; } = HttpHeaderNames.IdempotencyKey;

    /// <summary>HTTP methods for which idempotency is enforced. GET is never valid.</summary>
    /// <remarks>
    /// Exposed as <see cref="ISet{T}"/> so per-endpoint <see cref="IdempotencyMetadata.Configure"/>
    /// delegates may add or remove individual entries without replacing the whole set. The
    /// middleware clones the set per request before invoking the delegate, so consumer mutation
    /// is safe and does not leak to other requests.
    /// </remarks>
    public ISet<string> Methods { get; set; } = new HashSet<string>(
        ["POST", "PUT", "PATCH", "DELETE"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>How concurrent in-flight requests with the same key are handled.</summary>
    public InFlightStrategy InFlightStrategy { get; set; } = InFlightStrategy.Reject;

    /// <summary>How long to wait when <see cref="InFlightStrategy"/> is <see cref="InFlightStrategy.WaitAndReplay"/>. Defaults to 30 seconds.</summary>
    public TimeSpan InFlightLockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum body size in bytes eligible for fingerprinting. Defaults to 1 MiB. Capped at 64 MiB.</summary>
    public int MaxBodySizeForHashing { get; set; } = 1 * 1024 * 1024;

    /// <summary>How requests whose body exceeds <see cref="MaxBodySizeForHashing"/> are handled.</summary>
    public OversizeBehavior OversizeBehavior { get; set; } = OversizeBehavior.Reject;

    /// <summary>Status code returned when the same key is reused with a different body. Must be 409 or 422. Defaults to 422.</summary>
    public int MismatchStatusCode { get; set; } = StatusCodes.Status422UnprocessableEntity;

    /// <summary>
    /// Response headers copied into the cached record at capture time (and replayed verbatim).
    /// Headers not in this set are dropped at capture; <c>Set-Cookie</c> and <c>traceparent</c> are excluded by design.
    /// </summary>
    /// <remarks>
    /// Exposed as <see cref="ISet{T}"/> so per-endpoint overrides can extend or trim the allowlist
    /// in place. The middleware clones the set per request before delegate invocation.
    /// </remarks>
    public ISet<string> ReplayHeaderAllowlist { get; set; } = new HashSet<string>(
        ["Content-Type", "Content-Language", "Content-Encoding", "Content-Disposition", "Location", "Link", "ETag", "Last-Modified", "Cache-Control", "Vary"],
        StringComparer.OrdinalIgnoreCase);

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
    /// When <see langword="null"/>, the default <c>idem:{tenant}:{userId}:{method}:{path}:{key}</c>
    /// derivation is used.
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
    internal IdempotencyOptions Clone() => new()
    {
        IdempotencyKeyExpiration = IdempotencyKeyExpiration,
        HeaderName = HeaderName,
        Methods = new HashSet<string>(Methods, StringComparer.OrdinalIgnoreCase),
        InFlightStrategy = InFlightStrategy,
        InFlightLockTimeout = InFlightLockTimeout,
        MaxBodySizeForHashing = MaxBodySizeForHashing,
        OversizeBehavior = OversizeBehavior,
        MismatchStatusCode = MismatchStatusCode,
        ReplayHeaderAllowlist = new HashSet<string>(ReplayHeaderAllowlist, StringComparer.OrdinalIgnoreCase),
        ShouldCacheResponse = ShouldCacheResponse,
        ShouldApply = ShouldApply,
        KeyDeriver = KeyDeriver,
        RequestFingerprint = RequestFingerprint,
    };
}

internal sealed class IdempotencyOptionsValidator : AbstractValidator<IdempotencyOptions>
{
    private const int _MaxBodySizeForHashingCap = 64 * 1024 * 1024; // 64 MiB

    private static readonly HashSet<string> _ValidMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "CONNECT", "TRACE",
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
        When(x => x.InFlightStrategy == InFlightStrategy.WaitAndReplay, () =>
        {
            RuleFor(x => x.InFlightLockTimeout).LessThanOrEqualTo(TimeSpan.FromMinutes(5));
        });
    }
}

/// <summary>
/// DI-aware validator that fails fast at host startup when
/// <see cref="IdempotencyOptions.InFlightStrategy"/> is
/// <see cref="InFlightStrategy.WaitAndReplay"/> but no
/// <see cref="IDistributedLockProvider"/> is registered.
/// </summary>
internal sealed class IdempotencyOptionsDIValidator(IServiceProvider serviceProvider)
    : IValidateOptions<IdempotencyOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyOptions options)
    {
        if (options.InFlightStrategy != InFlightStrategy.WaitAndReplay)
        {
            return ValidateOptionsResult.Success;
        }

        var lockProvider = serviceProvider.GetService<IDistributedLockProvider>();

        if (lockProvider is not null)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{nameof(IdempotencyOptions)}.{nameof(IdempotencyOptions.InFlightStrategy)} = "
            + $"{nameof(InFlightStrategy.WaitAndReplay)} requires {nameof(IDistributedLockProvider)} "
            + "to be registered. Either switch InFlightStrategy to Reject or register a distributed-lock provider."
        );
    }
}
