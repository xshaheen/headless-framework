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

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("knobs.all", "v7")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("knobs.defaults", "1")]
namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var functions = new Dictionary<string, JobFunctionRegistration>(2);
            functions.Add("knobs.all", new JobFunctionRegistration { CronExpression = "*/5 * * * * *", Priority = (JobPriority)3, Delegate = Invoke_Demo_Knobs_AllKnobs, MaxConcurrency = 4, JobType = typeof(global::Demo.Knobs.AllKnobs), TimeZoneId = "Africa/Cairo", OnMissedRun = (MissedRunPolicy)1, MissedRunGraceSeconds = 90, OnOverlap = (CronOverlapPolicy)1 });
            functions.Add("knobs.defaults", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Demo_Knobs_Defaults, MaxConcurrency = 0, JobType = typeof(global::Demo.Knobs.Defaults) });
            catalog.AddFunctions(functions);
            RegisterRequestTypes(catalog);
            RegisterDescriptors(catalog);
        }

        private static void RegisterDescriptors(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(2);
            descriptors.Add("knobs.all", new JobFunctionDescriptor("knobs.all", null, "*/5 * * * * *", (JobPriority)3, 4, "v7"));
            descriptors.Add("knobs.defaults", new JobFunctionDescriptor("knobs.defaults", null, "", (JobPriority)0, 0, "1"));
            catalog.AddDescriptors(descriptors);
        }

        private static void RegisterRequestTypes(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
        }

        private static async Task Invoke_Demo_Knobs_AllKnobs(IServiceProvider serviceProvider, global::Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken)
        {
            var job = ActivatorUtilities.CreateInstance<global::Demo.Knobs.AllKnobs>(serviceProvider);
            await ((global::Headless.Jobs.Base.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }

        private static async Task Invoke_Demo_Knobs_Defaults(IServiceProvider serviceProvider, global::Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken)
        {
            var job = ActivatorUtilities.CreateInstance<global::Demo.Knobs.Defaults>(serviceProvider);
            await ((global::Headless.Jobs.Base.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
