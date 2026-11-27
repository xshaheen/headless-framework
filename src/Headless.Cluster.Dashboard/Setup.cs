// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Cluster;

public static class SetupClusterDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Cluster module to the unified dashboard under <see cref="ClusterDashboardOptions.ModuleId"/>: the
        /// incarnations Coordination's membership store reports with their liveness and metadata agreement, this
        /// node's membership event log, and the serving host's health checks with their recent probe results.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The module serves whichever half the host registers. Coordination counts once
        /// <c>AddHeadlessCoordination(...)</c> configures a provider. Health checks count once <c>AddHealthChecks()</c>
        /// ran. A host with neither fails to start with a message naming both calls.
        /// </para>
        /// <para>
        /// When health checks are already registered at this call, it also registers the health check monitor
        /// (<c>AddHeadlessHealthCheckMonitor()</c>), which the Health view and the probe commands read. A host that
        /// registers its health checks after this call registers the monitor itself.
        /// </para>
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>cluster</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddCluster()
        {
            return dashboard.AddCluster(static _ => { });
        }

        /// <summary>Adds the Cluster module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's policies and change reads.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>cluster</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddCluster(Action<ClusterDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<ClusterDashboardOptions, ClusterDashboardOptionsValidator>(setupAction);

            return _AddClusterModule(dashboard);
        }

        /// <summary>Adds the Cluster module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies and change reads.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>cluster</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddCluster(Action<ClusterDashboardOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<ClusterDashboardOptions, ClusterDashboardOptionsValidator>(setupAction);

            return _AddClusterModule(dashboard);
        }

        /// <summary>
        /// Adds the Cluster module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c>,
        /// <c>OperatePolicy</c>, and <c>ChangePageSize</c>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>cluster</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddCluster(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<ClusterDashboardOptions, ClusterDashboardOptionsValidator>(configuration);

            return _AddClusterModule(dashboard);
        }
    }

    private static HeadlessDashboardSetupBuilder _AddClusterModule(HeadlessDashboardSetupBuilder dashboard)
    {
        var services = dashboard.Services;

        // A health-only host then works with this one call. The check reads the collection as it stands, so health
        // checks registered later need the monitor registered by the host, which the Health view says in place.
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(HealthCheckService)))
        {
            services.AddHeadlessHealthCheckMonitor();
        }

        services.AddStartupValidator<ClusterDashboardStartupValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(static provider => new ClusterServices(provider));
        services.TryAddSingleton(static provider => new ClusterModule(
            provider.GetRequiredService<ClusterServices>(),
            provider.GetRequiredService<IDashboardEventPublisher>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<ClusterDashboardOptions>>().Value
        ));

        return dashboard.AddModule<ClusterModule, ClusterDashboardOptions>(ClusterDashboardOptions.ModuleId);
    }
}
