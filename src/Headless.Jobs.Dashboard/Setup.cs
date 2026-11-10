// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Jobs;

public static class SetupJobsDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Jobs module to the unified dashboard under <see cref="JobsDashboardOptions.ModuleId"/>: runs, cron
        /// jobs, functions, and nodes of the store <c>AddHeadlessJobs</c> configured, with Needs you, Overview figures,
        /// live changes from every node, commands, and Operations.
        /// </summary>
        /// <remarks>
        /// The module reads the entity types <c>AddHeadlessJobs&lt;TTimeJob, TCronJob&gt;</c> registered, so it takes
        /// none. A host that adds it without calling <c>AddHeadlessJobs</c> fails to start with a message naming that
        /// call.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>jobs</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddJobs()
        {
            return dashboard.AddJobs(static _ => { });
        }

        /// <summary>Adds the Jobs module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's policies and change reads.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>jobs</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddJobs(Action<JobsDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<JobsDashboardOptions, JobsDashboardOptionsValidator>(setupAction);

            return _AddJobsModule(dashboard);
        }

        /// <summary>Adds the Jobs module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies and change reads.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>jobs</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddJobs(Action<JobsDashboardOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<JobsDashboardOptions, JobsDashboardOptionsValidator>(setupAction);

            return _AddJobsModule(dashboard);
        }

        /// <summary>
        /// Adds the Jobs module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c>,
        /// <c>OperatePolicy</c>, <c>ChangeOverlapWindow</c>, and <c>ChangePageSize</c>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>jobs</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddJobs(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<JobsDashboardOptions, JobsDashboardOptionsValidator>(configuration);

            return _AddJobsModule(dashboard);
        }
    }

    private static HeadlessDashboardSetupBuilder _AddJobsModule(HeadlessDashboardSetupBuilder dashboard)
    {
        var services = dashboard.Services;

        // The module reaches Jobs through the seam AddHeadlessJobs registers; without it nothing could serve a request,
        // so the host fails at start rather than on the first dashboard call.
        services.RequireRegisteredService<IJobsDashboardBackend>(
            "the Jobs dashboard module (AddJobs)",
            "Call services.AddHeadlessJobs(...) to register Headless Jobs."
        );

        services.TryAddSingleton(provider => new JobsChangePublisher(
            _Backend(provider),
            provider.GetRequiredService<IDashboardEventPublisher>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<JobsChangePublisher>>()
        ));

        // Replace, not TryAdd: AddHeadlessJobs may have registered its no-op sender first; when it runs after this
        // call, its TryAdd keeps this one.
        // The module reads the host's last exception from the same instance the scheduler notifies.
        services.TryAddSingleton<JobsDashboardEventNotifier>();
        services.Replace(
            ServiceDescriptor.Singleton<IJobsNotificationHubSender>(provider =>
                provider.GetRequiredService<JobsDashboardEventNotifier>()
            )
        );

        services.TryAddSingleton(provider => new JobsModule(
            _Backend(provider),
            provider.GetRequiredService<JobsChangePublisher>(),
            provider.GetRequiredService<JobsDashboardEventNotifier>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<JobsDashboardOptions>>().Value
        ));

        return dashboard.AddModule<JobsModule, JobsDashboardOptions>(JobsDashboardOptions.ModuleId);
    }

    private const string _MissingJobs =
        "The Jobs dashboard module (AddJobs) needs Headless Jobs. Call services.AddHeadlessJobs(...) to register it.";

    // The shell may build the module while it maps endpoints, before startup validation reports the missing call, so
    // the module names it too.
    private static IJobsDashboardBackend _Backend(IServiceProvider provider) =>
        provider.GetService<IJobsDashboardBackend>() ?? throw new InvalidOperationException(_MissingJobs);
}
