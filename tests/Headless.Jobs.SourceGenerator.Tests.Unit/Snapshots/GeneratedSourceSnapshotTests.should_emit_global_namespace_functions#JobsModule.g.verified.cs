//HintName: JobsModule.g.cs
//Jobs readonly auto-generated file.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Jobs.SourceGenerator.Tests;

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("global.run", "1")]
namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c> inside <c>AddHeadlessJobs</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register()
        {
            var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>(1);
            jobFunctionDelegateDict.Add("global.run", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate(async (serviceProvider, context, cancellationToken) =>
            {
                await CreateGlobalJobs(serviceProvider).RunAsync(context, cancellationToken);
            }), MaxConcurrency = 0 });
            JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, 1);
            RegisterRequestTypes();
            RegisterDescriptors();
        }

        private static void RegisterDescriptors()
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(1);
            descriptors.Add("global.run", AppJobs.global_u002E_run);
            JobFunctionProvider.RegisterDescriptors(descriptors, 1);
        }

        private static global::GlobalJobs CreateGlobalJobs(IServiceProvider serviceProvider)
        {
            return new global::GlobalJobs();
        }

        private static void RegisterRequestTypes()
        {
        }
    }
}

namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Canonical generated handles for this assembly's requestless jobs.</summary>
    public static class AppJobs
    {
        /// <summary>A canonical requestless job descriptor.</summary>
        public static JobFunctionDescriptor global_u002E_run { get; } = new JobFunctionDescriptor("global.run", null, "", (JobPriority)0, 0, "1");
    }
}
