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
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var functions = new Dictionary<string, JobFunctionRegistration>(1);
            functions.Add("global.run", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_GlobalJob, MaxConcurrency = 0, JobType = typeof(global::GlobalJob) });
            catalog.AddFunctions(functions);
            RegisterRequestTypes(catalog);
            RegisterDescriptors(catalog);
        }

        private static void RegisterDescriptors(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(1);
            descriptors.Add("global.run", new JobFunctionDescriptor("global.run", null, "", (JobPriority)0, 0, "1"));
            catalog.AddDescriptors(descriptors);
        }

        private static void RegisterRequestTypes(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
        }

        private static async Task Invoke_GlobalJob(IServiceProvider serviceProvider, global::Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken)
        {
            var job = ActivatorUtilities.CreateInstance<global::GlobalJob>(serviceProvider);
            await ((global::Headless.Jobs.Base.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
