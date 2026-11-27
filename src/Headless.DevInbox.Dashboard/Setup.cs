// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DevInbox.Dashboard;
using Headless.DevInbox.Dashboard.Internal;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard;

/// <summary>Adds the Dev inbox module to the unified dashboard.</summary>
[PublicAPI]
public static class SetupDevInboxDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Dev inbox module to the unified dashboard under <see cref="DevInboxDashboardOptions.ModuleId"/>:
        /// the emails, SMS, and push notifications the host captured with <c>UseDevelopment()</c>, with search, live
        /// updates, previews, downloads, and Delete and Clear commands.
        /// </summary>
        /// <remarks>
        /// The module serves each capture store the host registered, in any combination. A host whose environment is
        /// not Development, or that registers no capture store, fails to start, because captured messages hold reset
        /// links and one-time codes. Call it only when <c>builder.Environment.IsDevelopment()</c> is true.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>dev-inbox</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddDevInbox()
        {
            return dashboard.AddDevInbox(static _ => { });
        }

        /// <summary>Adds the Dev inbox module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's view and operate policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>dev-inbox</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddDevInbox(Action<DevInboxDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<DevInboxDashboardOptions, DevInboxDashboardOptionsValidator>(setupAction);

            return _AddDevInboxModule(dashboard);
        }

        /// <summary>Adds the Dev inbox module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's view and operate policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>dev-inbox</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddDevInbox(Action<DevInboxDashboardOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<DevInboxDashboardOptions, DevInboxDashboardOptionsValidator>(setupAction);

            return _AddDevInboxModule(dashboard);
        }

        /// <summary>
        /// Adds the Dev inbox module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c> and
        /// <c>OperatePolicy</c>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>dev-inbox</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddDevInbox(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<DevInboxDashboardOptions, DevInboxDashboardOptionsValidator>(configuration);

            return _AddDevInboxModule(dashboard);
        }
    }

    private static HeadlessDashboardSetupBuilder _AddDevInboxModule(HeadlessDashboardSetupBuilder dashboard)
    {
        var services = dashboard.Services;

        // The environment is not built while services register, so the Development check runs when the host starts.
        services.AddStartupValidator<DevInboxStartupValidator>();

        // The shell may build the module while it maps endpoints, before startup validation runs, so the module's
        // factory refuses the same hosts.
        services.TryAddSingleton(static provider => new DevInboxModule(
            DevInboxStores.Resolve(provider),
            provider.GetRequiredService<IDashboardEventPublisher>()
        ));

        return dashboard.AddModule<DevInboxModule, DevInboxDashboardOptions>(DevInboxDashboardOptions.ModuleId);
    }
}
