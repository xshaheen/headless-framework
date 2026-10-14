// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Idempotency;

/// <summary>Endpoint metadata that carries per-endpoint idempotency option overrides.</summary>
/// <remarks>
/// Delegates configured here should be stateless or resolve scoped services from
/// <c>HttpContext.RequestServices</c>. Capturing a service instance from a different scope
/// produces incorrect behavior or <see cref="ObjectDisposedException"/> at runtime.
/// </remarks>
[PublicAPI]
public sealed class HttpIdempotencyMetadata
{
    /// <summary>Initializes a new instance with the given option-override delegate.</summary>
    /// <param name="configure">
    /// Delegate applied once per request to a fresh clone of <see cref="HttpIdempotencyOptions"/>.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public HttpIdempotencyMetadata(Action<HttpIdempotencyOptions> configure)
    {
        Argument.IsNotNull(configure);
        Configure = configure;
    }

    /// <summary>
    /// Delegate invoked once per metadata instance (cached by reference) against a fresh clone of
    /// the application-level options. All scalar properties (<see cref="HttpIdempotencyOptions.Retention"/>,
    /// <see cref="HttpIdempotencyOptions.InFlightStrategy"/>, <see cref="HttpIdempotencyOptions.MismatchStatusCode"/>,
    /// etc.) and collection/delegate properties (<see cref="HttpIdempotencyOptions.Methods"/>,
    /// <see cref="HttpIdempotencyOptions.ReplayHeaderAllowlist"/>, <see cref="HttpIdempotencyOptions.KeyDeriver"/>,
    /// <see cref="HttpIdempotencyOptions.RequestFingerprint"/>, <see cref="HttpIdempotencyOptions.ShouldApply"/>,
    /// <see cref="HttpIdempotencyOptions.ShouldCacheResponse"/>) are honored.
    /// </summary>
    /// <remarks>
    /// <see cref="HttpIdempotencyOptions.HeaderName"/> overrides are ignored — the middleware reads the
    /// idempotency-key request header before resolving endpoint metadata.
    /// </remarks>
    public Action<HttpIdempotencyOptions> Configure { get; }
}
