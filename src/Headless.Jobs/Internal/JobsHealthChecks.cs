// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Jobs.Internal;

/// <summary>
/// The readiness checks a Jobs host that runs background services contributes: <c>jobs</c> reports whether the host can
/// process jobs at all, and <c>jobs-scheduler</c> whether its scheduler loop is dispatching.
/// </summary>
internal static class JobsHealthChecks
{
    public const string HostCheckName = "jobs";

    public const string SchedulerCheckName = "jobs-scheduler";

    public static void Add(IServiceCollection services)
    {
        // Unhealthy until the startup drain opens the activation barrier, and again from the moment host shutdown
        // begins: the host stops claiming work then, so a load balancer should stop routing to it before the drain.
        services.AddHeadlessHealthCheck(
            HostCheckName,
            static (provider, _) =>
            {
                if (!provider.GetRequiredService<JobsActivationBarrier>().IsActivated)
                {
                    throw new InvalidOperationException("Jobs activation has not completed.");
                }

                // Absent outside a generic host, where nothing stops the application either.
                if (
                    provider.GetService<IHostApplicationLifetime>()?.ApplicationStopping.IsCancellationRequested == true
                )
                {
                    throw new InvalidOperationException("The Jobs host is shutting down.");
                }

                return Task.CompletedTask;
            },
            HeadlessHealthCheckTags.Jobs
        );

        // Degraded rather than Unhealthy: an operator stop or manual start mode is deliberate, so the pod stays in
        // rotation while the check tells the operator this host dispatches nothing.
        services.AddHeadlessHealthCheck(
            SchedulerCheckName,
            static (provider, _) =>
                provider.GetRequiredService<IJobsHostScheduler>().IsRunning
                    ? Task.CompletedTask
                    : throw new InvalidOperationException("The Jobs scheduler is not running."),
            HeadlessHealthCheckTags.Jobs
        );

        // Inserted first so it is only a default: an application's ConfigureHeadlessHealthCheck call for this check
        // overrides it whether it runs before or after AddHeadlessJobs.
        services.Insert(
            0,
            ServiceDescriptor.Singleton<IConfigureOptions<HeadlessHealthCheckOptions>>(
                new ConfigureNamedOptions<HeadlessHealthCheckOptions>(
                    SchedulerCheckName,
                    static options => options.FailureStatus = HealthStatus.Degraded
                )
            )
        );
    }
}
