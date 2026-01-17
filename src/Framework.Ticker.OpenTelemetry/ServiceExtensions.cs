using Framework.Ticker.Utilities;
using Framework.Ticker.Utilities.Entities;
using Framework.Ticker.Utilities.Instrumentation;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Ticker.Instrumentation.OpenTelemetry;

public static class ServiceExtensions
{
    /// <summary>
    /// Adds OpenTelemetry instrumentation with activity tracing for TickerQ jobs.
    /// Also includes standard logging through ILogger.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddOpenTelemetryInstrumentation<
        TTimeTicker,
        TCronTicker
    >(this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            // Replace any existing instrumentation with OpenTelemetry version
            services.AddSingleton<ITickerQInstrumentation, OpenTelemetryInstrumentation>();
        };

        return tickerConfiguration;
    }
}
