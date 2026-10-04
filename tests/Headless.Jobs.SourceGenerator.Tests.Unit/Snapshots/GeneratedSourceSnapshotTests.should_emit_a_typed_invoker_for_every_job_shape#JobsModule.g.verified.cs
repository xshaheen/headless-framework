//HintName: JobsModule.g.cs
//Jobs readonly auto-generated file.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Headless.Jobs;
using Jobs.SourceGenerator.Tests;

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("shapes.async-disposable", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("shapes.disposable", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("shapes.explicit", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("shapes.plain", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("shapes.typed", "1")]
namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var functions = new Dictionary<string, JobFunctionRegistration>(5);
            functions.Add("shapes.async-disposable", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Demo_Shapes_AsyncDisposableJob, MaxConcurrency = 0, JobType = typeof(global::Demo.Shapes.AsyncDisposableJob) });
            functions.Add("shapes.disposable", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Demo_Shapes_DisposableJob, MaxConcurrency = 0, JobType = typeof(global::Demo.Shapes.DisposableJob) });
            functions.Add("shapes.explicit", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Demo_Shapes_ExplicitJob, MaxConcurrency = 0, JobType = typeof(global::Demo.Shapes.ExplicitJob) });
            functions.Add("shapes.plain", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Demo_Shapes_CloseDay, MaxConcurrency = 0, JobType = typeof(global::Demo.Shapes.CloseDay) });
            functions.Add("shapes.typed", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Demo_Shapes_SendInvoice, MaxConcurrency = 0, JobType = typeof(global::Demo.Shapes.SendInvoice) });
            catalog.AddFunctions(functions);
            RegisterRequestTypes(catalog);
            RegisterDescriptors(catalog);
        }

        private static void RegisterDescriptors(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(5);
            descriptors.Add("shapes.async-disposable", new JobFunctionDescriptor("shapes.async-disposable", null, "", (JobPriority)0, 0, "1"));
            descriptors.Add("shapes.disposable", new JobFunctionDescriptor("shapes.disposable", null, "", (JobPriority)0, 0, "1"));
            descriptors.Add("shapes.explicit", new JobFunctionDescriptor("shapes.explicit", null, "", (JobPriority)0, 0, "1"));
            descriptors.Add("shapes.plain", new JobFunctionDescriptor("shapes.plain", null, "", (JobPriority)0, 0, "1"));
            descriptors.Add("shapes.typed", new JobFunctionDescriptor("shapes.typed", typeof(global::Demo.Shapes.InvoiceArgs), "", (JobPriority)0, 0, "1"));
            catalog.AddDescriptors(descriptors);
        }

        private static void RegisterRequestTypes(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var requestTypes = new Dictionary<string, (string, Type)>(1);
            requestTypes.Add("shapes.typed", (typeof(global::Demo.Shapes.InvoiceArgs).FullName, typeof(global::Demo.Shapes.InvoiceArgs)));
            catalog.AddRequestTypes(requestTypes);
        }

        private static async Task Invoke_Demo_Shapes_AsyncDisposableJob(IServiceProvider serviceProvider, global::Headless.Jobs.JobContext context, CancellationToken cancellationToken)
        {
            var job = ActivatorUtilities.CreateInstance<global::Demo.Shapes.AsyncDisposableJob>(serviceProvider);
            await using (job.ConfigureAwait(false))
            {
                await ((global::Headless.Jobs.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task Invoke_Demo_Shapes_DisposableJob(IServiceProvider serviceProvider, global::Headless.Jobs.JobContext context, CancellationToken cancellationToken)
        {
            using var job = ActivatorUtilities.CreateInstance<global::Demo.Shapes.DisposableJob>(serviceProvider);
            await ((global::Headless.Jobs.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }

        private static async Task Invoke_Demo_Shapes_ExplicitJob(IServiceProvider serviceProvider, global::Headless.Jobs.JobContext context, CancellationToken cancellationToken)
        {
            var job = ActivatorUtilities.CreateInstance<global::Demo.Shapes.ExplicitJob>(serviceProvider);
            await ((global::Headless.Jobs.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }

        private static async Task Invoke_Demo_Shapes_CloseDay(IServiceProvider serviceProvider, global::Headless.Jobs.JobContext context, CancellationToken cancellationToken)
        {
            var job = ActivatorUtilities.CreateInstance<global::Demo.Shapes.CloseDay>(serviceProvider);
            await ((global::Headless.Jobs.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }

        private static async Task Invoke_Demo_Shapes_SendInvoice(IServiceProvider serviceProvider, global::Headless.Jobs.JobContext context, CancellationToken cancellationToken)
        {
            var request = await JobsRequestProvider.GetRequestAsync<global::Demo.Shapes.InvoiceArgs>(context, cancellationToken).ConfigureAwait(false);
            var job = ActivatorUtilities.CreateInstance<global::Demo.Shapes.SendInvoice>(serviceProvider);
            await ((global::Headless.Jobs.IJob<global::Demo.Shapes.InvoiceArgs>)job).ExecuteAsync(new global::Headless.Jobs.JobContext<global::Demo.Shapes.InvoiceArgs>(context, request), cancellationToken).ConfigureAwait(false);
        }
    }
}
