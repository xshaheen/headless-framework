// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Leases;

public static class SetupLeasesDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Leases module to the unified dashboard under <see cref="LeasesDashboardOptions.ModuleId"/>, in the
        /// <c>platform</c> product: the fenced leases the fencing store holds, by state, tenant, and kind, and the
        /// distributed locks the registered lock provider can list. The module only reads; it never changes a lease or
        /// a lock.
        /// </summary>
        /// <remarks>
        /// The module serves whichever half the host registers. Fenced leases count once <c>AddHeadlessFencing(...)</c>
        /// ran. Distributed locks count once <c>AddHeadlessDistributedLocks(...)</c> registered a provider; a
        /// <c>NullDistributedLock</c> does not count. A host with neither fails to start with a message naming both
        /// calls.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>leases</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddLeases()
        {
            return dashboard.AddLeases(static _ => { });
        }

        /// <summary>Adds the Leases module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>leases</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddLeases(Action<LeasesDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<LeasesDashboardOptions, LeasesDashboardOptionsValidator>(setupAction);

            return _AddLeasesModule(dashboard);
        }

        /// <summary>Adds the Leases module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>leases</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddLeases(Action<LeasesDashboardOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<LeasesDashboardOptions, LeasesDashboardOptionsValidator>(setupAction);

            return _AddLeasesModule(dashboard);
        }

        /// <summary>
        /// Adds the Leases module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c> and
        /// <c>OperatePolicy</c>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>leases</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddLeases(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<LeasesDashboardOptions, LeasesDashboardOptionsValidator>(configuration);

            return _AddLeasesModule(dashboard);
        }
    }

    private static HeadlessDashboardSetupBuilder _AddLeasesModule(HeadlessDashboardSetupBuilder dashboard)
    {
        var services = dashboard.Services;

        services.AddStartupValidator<LeasesDashboardStartupValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(static provider => new LeasesServices(provider));
        services.TryAddSingleton(static provider => new LeaseKindsCache(provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(static provider => new LeasesModule(
            provider.GetRequiredService<LeasesServices>(),
            provider.GetRequiredService<LeaseKindsCache>(),
            provider.GetRequiredService<TimeProvider>()
        ));

        return dashboard.AddModule<LeasesModule, LeasesDashboardOptions>(LeasesDashboardOptions.ModuleId);
    }
}
