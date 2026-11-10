// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard;

/// <summary>Registers the services and maps the endpoint of the dashboard's live stream.</summary>
internal static class DashboardStreamingSetup
{
    /// <summary>
    /// Registers the node's event ring and epoch, the <see cref="IDashboardEventPublisher"/> modules publish through,
    /// and the change-reader coordinator. Nothing here runs at startup: the readers start with the node's first stream.
    /// Streams time heartbeats, coalescing, and reads with the registered <see cref="TimeProvider"/>.
    /// </summary>
    internal static void AddStreaming(IServiceCollection services)
    {
        Argument.IsNotNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<NodeEpoch>();
        services.TryAddSingleton<EventRingBuffer>();
        services.TryAddSingleton<DashboardEventPublisher>();
        services.TryAddSingleton<IDashboardEventPublisher>(static provider =>
            provider.GetRequiredService<DashboardEventPublisher>()
        );
        services.TryAddSingleton<ChangeReaderCoordinator>();
    }

    /// <summary>Maps <c>api/stream</c> on <paramref name="dashboard"/>, the group at the base path.</summary>
    internal static void MapStreaming(RouteGroupBuilder dashboard)
    {
        Argument.IsNotNull(dashboard);

        StreamEndpoint.Map(dashboard);
    }
}
