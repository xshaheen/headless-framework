using System.Reflection;
using Framework.Ticker.Utilities;
using Framework.Ticker.Utilities.Entities;

namespace Framework.Ticker.DependencyInjection;

public static class TickerQDiscoveryExtension
{
    private const string _GeneratedClassSuffix = "TickerQInstanceFactoryExtensions";

    /// <summary>
    /// Loads the assemblies to initialize the source generated code.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerQDiscovery<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Assembly[] assemblies
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var assembliesToLoad = assemblies ?? [];

        foreach (var assembly in assembliesToLoad)
        {
            if (!string.IsNullOrEmpty(assembly.FullName))
                Assembly.Load(assembly.FullName);
        }

        return tickerConfiguration;
    }
}
