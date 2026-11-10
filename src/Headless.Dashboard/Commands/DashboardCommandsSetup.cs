// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard;

/// <summary>Registers the services and maps the endpoints of server commands and Operations.</summary>
internal static class DashboardCommandsSetup
{
    /// <summary>
    /// Registers the operator log with its default <c>ILogger</c> sink, the Operation scope token protector, and the
    /// clock tokens expire by. Hosts add more sinks by registering further <see cref="IDashboardOperatorLog"/>
    /// implementations.
    /// </summary>
    internal static void AddCommands(IServiceCollection services)
    {
        Argument.IsNotNull(services);

        // Operation tokens are protected with the host's key ring, which every node of the host must share.
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDashboardOperatorLog, LoggerOperatorLog>());
        services.TryAddSingleton<OperatorLogWriter>();
        services.TryAddSingleton<OperationScopeTokenProtector>();
    }

    /// <summary>
    /// Maps the command and Operation endpoints of every loaded module that declares commands on
    /// <paramref name="dashboard"/>, the group at the dashboard's base path. A module refused for its SDK version maps
    /// none, so the server withholds the commands its page cannot show.
    /// </summary>
    internal static void MapCommands(RouteGroupBuilder dashboard)
    {
        Argument.IsNotNull(dashboard);

        var manifest = ((IEndpointRouteBuilder)dashboard)
            .ServiceProvider.GetRequiredService<DashboardManifestProvider>()
            .Manifest;

        foreach (var module in manifest.Modules)
        {
            if (module.IsRefused || module.Module is not IDashboardCommandSource)
            {
                continue;
            }

            CommandEndpoints.Map(dashboard, module);
        }
    }
}
