// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>Endpoint metadata that carries per-endpoint idempotency option overrides.</summary>
/// <remarks>
/// Delegates configured here should be stateless or resolve scoped services from
/// <c>HttpContext.RequestServices</c>. Capturing a service instance from a different scope
/// produces incorrect behavior or <see cref="ObjectDisposedException"/> at runtime.
/// </remarks>
[PublicAPI]
public sealed class IdempotencyMetadata
{
    /// <summary>Initializes a new instance with the given option-override delegate.</summary>
    /// <param name="configure">
    /// Delegate applied once per request to a fresh clone of <see cref="IdempotencyOptions"/>.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public IdempotencyMetadata(Action<IdempotencyOptions> configure)
    {
        Argument.IsNotNull(configure);
        Configure = configure;
    }

    /// <summary>
    /// Delegate invoked once per metadata instance (cached by reference) against a fresh clone of
    /// the application-level options. All scalar properties (<see cref="IdempotencyOptions.IdempotencyKeyExpiration"/>,
    /// <see cref="IdempotencyOptions.InFlightStrategy"/>, <see cref="IdempotencyOptions.MismatchStatusCode"/>,
    /// etc.) and collection/delegate properties (<see cref="IdempotencyOptions.Methods"/>,
    /// <see cref="IdempotencyOptions.ReplayHeaderAllowlist"/>, <see cref="IdempotencyOptions.KeyDeriver"/>,
    /// <see cref="IdempotencyOptions.RequestFingerprint"/>, <see cref="IdempotencyOptions.ShouldApply"/>,
    /// <see cref="IdempotencyOptions.ShouldCacheResponse"/>) are honored.
    /// </summary>
    /// <remarks>
    /// <see cref="IdempotencyOptions.HeaderName"/> overrides are ignored — the middleware reads the
    /// idempotency-key request header before resolving endpoint metadata.
    /// </remarks>
    public Action<IdempotencyOptions> Configure { get; }
}
