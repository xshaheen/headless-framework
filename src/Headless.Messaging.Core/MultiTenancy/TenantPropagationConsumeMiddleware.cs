// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.MultiTenancy;

/// <summary>Restores <see cref="ICurrentTenant"/> from the resolved consume tenant for the inner handler.</summary>
[PublicAPI]
public sealed class TenantPropagationConsumeMiddleware(
    ICurrentTenant currentTenant,
    ILogger<TenantPropagationConsumeMiddleware>? logger = null
) : IConsumeMiddleware<ConsumeContext>
{
    /// <summary>Framework priority for tenant restoration middleware.</summary>
    public const int Priority = -1000;

    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    /// <inheritdoc/>
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(next);

        if (context.TenantId is not { } value)
        {
            await next().ConfigureAwait(false);
            return;
        }

        logger?.TenantContextSwitched(value);

        using var scope = _currentTenant.Change(value);
        await next().ConfigureAwait(false);
    }
}
