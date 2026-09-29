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

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("1start", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("ToString", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("class", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("knobs.all", "v7")]
namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c> inside <c>AddHeadlessJobs</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register()
        {
            var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>(4);
            jobFunctionDelegateDict.Add("knobs.all", new JobFunctionRegistration { CronExpression = "*/5 * * * * *", Priority = (JobPriority)3, Delegate = new JobFunctionDelegate(async (serviceProvider, context, cancellationToken) =>
            {
                await CreateDemoKnobsKnobJobs(serviceProvider).AllAsync(cancellationToken);
            }), MaxConcurrency = 4, OnMissedRun = (MissedRunPolicy)1, MissedRunGraceSeconds = 90, OnOverlap = (CronOverlapPolicy)1 });
            jobFunctionDelegateDict.Add("class", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)2, Delegate = new JobFunctionDelegate((serviceProvider, context, cancellationToken) =>
            {
                CreateDemoKnobsKnobJobs(serviceProvider).Keyword();
                return Task.CompletedTask;
            }), MaxConcurrency = 0 });
            jobFunctionDelegateDict.Add("ToString", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate((serviceProvider, context, cancellationToken) =>
            {
                CreateDemoKnobsKnobJobs(serviceProvider).ObjectMember();
                return Task.CompletedTask;
            }), MaxConcurrency = 0 });
            jobFunctionDelegateDict.Add("1start", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate((serviceProvider, context, cancellationToken) =>
            {
                CreateDemoKnobsKnobJobs(serviceProvider).Digit();
                return Task.CompletedTask;
            }), MaxConcurrency = 0 });
            JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, 4);
            RegisterRequestTypes();
            RegisterDescriptors();
        }

        private static void RegisterDescriptors()
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(4);
            descriptors.Add("1start", AppJobs._u0031_start);
            descriptors.Add("ToString", AppJobs._u0054_oString);
            descriptors.Add("class", AppJobs.@class);
            descriptors.Add("knobs.all", AppJobs.knobs_u002E_all);
            JobFunctionProvider.RegisterDescriptors(descriptors, 4);
        }

        private static global::Demo.Knobs.KnobJobs CreateDemoKnobsKnobJobs(IServiceProvider serviceProvider)
        {
            return new global::Demo.Knobs.KnobJobs();
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
        public static JobFunctionDescriptor _u0031_start { get; } = new JobFunctionDescriptor("1start", null, "", (JobPriority)0, 0, "1");
        /// <summary>A canonical requestless job descriptor.</summary>
        public static JobFunctionDescriptor _u0054_oString { get; } = new JobFunctionDescriptor("ToString", null, "", (JobPriority)0, 0, "1");
        /// <summary>A canonical requestless job descriptor.</summary>
        public static JobFunctionDescriptor @class { get; } = new JobFunctionDescriptor("class", null, "", (JobPriority)2, 0, "1");
        /// <summary>A canonical requestless job descriptor.</summary>
        public static JobFunctionDescriptor knobs_u002E_all { get; } = new JobFunctionDescriptor("knobs.all", null, "*/5 * * * * *", (JobPriority)3, 4, "v7");
    }
}
