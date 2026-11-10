// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard;

/// <summary>Registers the services and maps the endpoints that aggregate every module's answers.</summary>
internal static class DashboardAggregationSetup
{
    /// <summary>
    /// Registers the module fan-out and payload formatting. The fan-out times modules with the registered
    /// <see cref="TimeProvider"/>, the system clock unless the host registers another.
    /// </summary>
    internal static void AddAggregation(IServiceCollection services)
    {
        Argument.IsNotNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ModuleFanOut>();
        services.TryAddSingleton<DashboardPayloadFormatting>();
    }

    /// <summary>Maps the aggregation endpoints on <paramref name="dashboard"/>, the group at the dashboard's base path.</summary>
    internal static void MapAggregation(RouteGroupBuilder dashboard)
    {
        Argument.IsNotNull(dashboard);

        AggregationEndpoints.Map(dashboard);
        PayloadEndpoints.Map(dashboard);
    }
}
