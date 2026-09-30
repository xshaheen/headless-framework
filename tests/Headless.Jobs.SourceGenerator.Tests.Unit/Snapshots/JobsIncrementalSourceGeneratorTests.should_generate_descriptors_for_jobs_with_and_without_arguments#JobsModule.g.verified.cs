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

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("invoice.cleanup", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("invoice.create", "schema-v2")]
namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register()
        {
            var functions = new Dictionary<string, JobFunctionRegistration>(2);
            functions.Add("invoice.cleanup", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Demo_CleanupJob, MaxConcurrency = 0, JobType = typeof(global::Demo.CleanupJob) });
            functions.Add("invoice.create", new JobFunctionRegistration { CronExpression = "0 */5 * * * *", Priority = (JobPriority)1, Delegate = Invoke_Demo_CreateInvoiceJob, MaxConcurrency = 3, JobType = typeof(global::Demo.CreateInvoiceJob) });
            JobFunctionProvider.RegisterFunctions(functions, 2);
            RegisterRequestTypes();
            RegisterDescriptors();
        }

        private static void RegisterDescriptors()
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(2);
            descriptors.Add("invoice.cleanup", new JobFunctionDescriptor("invoice.cleanup", null, "", (JobPriority)0, 0, "1"));
            descriptors.Add("invoice.create", new JobFunctionDescriptor("invoice.create", typeof(global::Demo.CreateInvoice), "0 */5 * * * *", (JobPriority)1, 3, "schema-v2"));
            JobFunctionProvider.RegisterDescriptors(descriptors, 2);
        }

        private static void RegisterRequestTypes()
        {
            var requestTypes = new Dictionary<string, (string, Type)>(1);
            requestTypes.Add("invoice.create", (typeof(global::Demo.CreateInvoice).FullName, typeof(global::Demo.CreateInvoice)));
            JobFunctionProvider.RegisterRequestType(requestTypes, 1);
        }

        private static async Task Invoke_Demo_CleanupJob(IServiceProvider serviceProvider, global::Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken)
        {
            var job = ActivatorUtilities.CreateInstance<global::Demo.CleanupJob>(serviceProvider);
            await ((global::Headless.Jobs.Base.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }

        private static async Task Invoke_Demo_CreateInvoiceJob(IServiceProvider serviceProvider, global::Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken)
        {
            var request = await JobsRequestProvider.GetRequestAsync<global::Demo.CreateInvoice>(context, cancellationToken).ConfigureAwait(false);
            var job = ActivatorUtilities.CreateInstance<global::Demo.CreateInvoiceJob>(serviceProvider);
            await ((global::Headless.Jobs.Base.IJob<global::Demo.CreateInvoice>)job).ExecuteAsync(new global::Headless.Jobs.Base.JobContext<global::Demo.CreateInvoice>(context, request), cancellationToken).ConfigureAwait(false);
        }
    }
}
