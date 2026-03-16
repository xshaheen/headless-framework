using Headless.Jobs.Entities;
using Headless.Jobs.Instrumentation;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

public static class OpenTelemetrySetup
{
    /// <summary>
    /// Adds OpenTelemetry instrumentation with activity tracing for Jobs jobs.
    /// Also includes standard logging through ILogger.
    /// </summary>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddOpenTelemetryInstrumentation<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        jobsConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            // Replace any existing instrumentation with OpenTelemetry version
            services.AddSingleton<IJobsInstrumentation, OpenTelemetryInstrumentation>();
        };

        return jobsConfiguration;
    }
}
