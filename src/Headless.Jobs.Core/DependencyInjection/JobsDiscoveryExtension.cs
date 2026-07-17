// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Jobs.Entities;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

public static class JobsDiscoveryExtension
{
    /// <summary>
    /// Forces the specified assemblies to load and runs their source-generated <c>ModuleInitializer</c>
    /// code so job functions and middleware are registered before the generated catalogs freeze.
    /// </summary>
    /// <remarks>
    /// The .NET runtime loads assemblies lazily. If a job function assembly is not otherwise
    /// referenced at startup, its <c>ModuleInitializer</c> will not execute before generated
    /// registrations freeze. Pass each assembly that contains <c>[JobFunction]</c> methods or
    /// assembly-level Jobs middleware here. Running a module constructor is runtime-idempotent.
    /// </remarks>
    /// <param name="jobsConfiguration">The jobs options builder.</param>
    /// <param name="assemblies">The assemblies to force-load.</param>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddJobsDiscovery<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        Assembly[]? assemblies
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var assembliesToLoad = assemblies ?? [];

        foreach (var assembly in assembliesToLoad)
        {
            if (!string.IsNullOrEmpty(assembly.FullName))
            {
                var loadedAssembly = Assembly.Load(assembly.FullName);
                RuntimeHelpers.RunModuleConstructor(loadedAssembly.ManifestModule.ModuleHandle);
            }
        }

        return jobsConfiguration;
    }
}
