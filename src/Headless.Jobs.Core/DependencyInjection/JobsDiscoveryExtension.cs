using System.Reflection;
using Headless.Jobs.Entities;

namespace Headless.Jobs.DependencyInjection;

public static class JobsDiscoveryExtension
{
    private const string _GeneratedClassSuffix = "JobsInstanceFactoryExtensions";

    /// <summary>
    /// Loads the assemblies to initialize the source generated code.
    /// </summary>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddJobsDiscovery<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        Assembly[] assemblies
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var assembliesToLoad = assemblies ?? [];

        foreach (var assembly in assembliesToLoad)
        {
            if (!string.IsNullOrEmpty(assembly.FullName))
            {
                Assembly.Load(assembly.FullName);
            }
        }

        return jobsConfiguration;
    }
}
