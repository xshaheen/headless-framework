using Headless.Jobs.Entities;
using Headless.Jobs.Instrumentation;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

public static class ServiceExtensions
{
    /// <summary>
    /// Adds OpenTelemetry instrumentation with activity tracing for Jobs jobs.
    /// Also includes standard logging through ILogger.
    /// </summary>
    public static JobsOptionsBuilder<TTimeTicker, TCronTicker> AddOpenTelemetryInstrumentation<
        TTimeTicker,
        TCronTicker
    >(this JobsOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration)
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            // Replace any existing instrumentation with OpenTelemetry version
            services.AddSingleton<IJobsInstrumentation, OpenTelemetryInstrumentation>();
        };

        return tickerConfiguration;
    }
}
