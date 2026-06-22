// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Jobs.Entities;

namespace Headless.Jobs.DependencyInjection;

public static class JobsDiscoveryExtension
{
    private const string _GeneratedClassSuffix = "JobsInstanceFactoryExtensions";

    /// <summary>
    /// Forces the specified assemblies to load so their source-generated <c>ModuleInitializer</c>
    /// code runs and registers job functions with <c>JobFunctionProvider</c>.
    /// </summary>
    /// <remarks>
    /// The .NET runtime loads assemblies lazily. If a job function assembly is not otherwise
    /// referenced at startup, its <c>ModuleInitializer</c> — and therefore its
    /// <c>JobFunctionProvider.RegisterFunctions</c> call — will not execute before
    /// <c>JobFunctionProvider.Build()</c> freezes the registry. Pass each assembly that contains
    /// <c>[JobFunction]</c>-annotated methods here to guarantee registration.
    /// </remarks>
    /// <param name="jobsConfiguration">The jobs options builder.</param>
    /// <param name="assemblies">The assemblies to force-load.</param>
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
