// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.MultiTenancy;

/// <summary>
/// Restores <see cref="ICurrentTenant"/> from the resolved consume tenant for the inner handler, and preloads that
/// tenant's data placement so a tenant-routed context injected into the consumer can be built.
/// </summary>
[PublicAPI]
public sealed class TenantPropagationConsumeMiddleware(
    ICurrentTenant currentTenant,
    ILogger<TenantPropagationConsumeMiddleware>? logger = null,
    IServiceProvider? services = null
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

        // The middleware is scoped, so services is the message scope the consumer is resolved from.
        if (services?.GetService<TenantDataPlacementPreloader>() is { } preloader)
        {
            await preloader
                .RunAsync(services, value, () => next().AsTask(), context.CancellationToken)
                .ConfigureAwait(false);

            return;
        }

        await next().ConfigureAwait(false);
    }
}
