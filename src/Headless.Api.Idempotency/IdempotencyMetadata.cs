// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api.Idempotency;

/// <summary>Endpoint metadata that carries per-endpoint idempotency option overrides.</summary>
/// <remarks>
/// Delegates configured here should be stateless or resolve scoped services from
/// <c>HttpContext.RequestServices</c>. Capturing a service instance from a different scope
/// produces incorrect behavior or <see cref="ObjectDisposedException"/> at runtime.
/// </remarks>
[PublicAPI]
public sealed class IdempotencyMetadata
{
    public IdempotencyMetadata(Action<IdempotencyOptions> configure)
    {
        Argument.IsNotNull(configure);
        Configure = configure;
    }

    public Action<IdempotencyOptions> Configure { get; }
}
