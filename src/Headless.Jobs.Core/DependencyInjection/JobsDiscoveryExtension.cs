using System.Reflection;
using Headless.Jobs.Entities;

namespace Headless.Jobs.DependencyInjection;

public static class JobsDiscoveryExtension
{
    private const string _GeneratedClassSuffix = "JobsInstanceFactoryExtensions";

    /// <summary>
    /// Loads the assemblies to initialize the source generated code.
    /// </summary>
    public static JobsOptionsBuilder<TTimeTicker, TCronTicker> AddJobsDiscovery<TTimeTicker, TCronTicker>(
        this JobsOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Assembly[] assemblies
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var assembliesToLoad = assemblies ?? [];

        foreach (var assembly in assembliesToLoad)
        {
            if (!string.IsNullOrEmpty(assembly.FullName))
            {
                Assembly.Load(assembly.FullName);
            }
        }

        return tickerConfiguration;
    }
}
