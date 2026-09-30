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

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("billing.run", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("root.payload", "1")]
namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var functions = new Dictionary<string, JobFunctionRegistration>(2);
            functions.Add("billing.run", new JobFunctionRegistration { CronExpression = "%Jobs:Billing:Cron", Priority = (JobPriority)0, Delegate = Invoke_Billing_BillingJob, MaxConcurrency = 0, JobType = typeof(global::Billing.BillingJob) });
            functions.Add("root.payload", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Jobs_SourceGenerator_Tests_RootJob, MaxConcurrency = 0, JobType = typeof(global::Jobs.SourceGenerator.Tests.RootJob) });
            catalog.AddFunctions(functions);
            RegisterRequestTypes(catalog);
            RegisterDescriptors(catalog);
        }

        private static void RegisterDescriptors(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(2);
            descriptors.Add("billing.run", new JobFunctionDescriptor("billing.run", typeof(global::Billing.Payload), "%Jobs:Billing:Cron", (JobPriority)0, 0, "1"));
            descriptors.Add("root.payload", new JobFunctionDescriptor("root.payload", typeof(global::Jobs.SourceGenerator.Tests.Payload), "", (JobPriority)0, 0, "1"));
            catalog.AddDescriptors(descriptors);
        }

        private static void RegisterRequestTypes(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var requestTypes = new Dictionary<string, (string, Type)>(2);
            requestTypes.Add("billing.run", (typeof(global::Billing.Payload).FullName, typeof(global::Billing.Payload)));
            requestTypes.Add("root.payload", (typeof(global::Jobs.SourceGenerator.Tests.Payload).FullName, typeof(global::Jobs.SourceGenerator.Tests.Payload)));
            catalog.AddRequestTypes(requestTypes);
        }

        private static async Task Invoke_Billing_BillingJob(IServiceProvider serviceProvider, global::Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken)
        {
            var request = await JobsRequestProvider.GetRequestAsync<global::Billing.Payload>(context, cancellationToken).ConfigureAwait(false);
            var job = ActivatorUtilities.CreateInstance<global::Billing.BillingJob>(serviceProvider);
            await ((global::Headless.Jobs.Base.IJob<global::Billing.Payload>)job).ExecuteAsync(new global::Headless.Jobs.Base.JobContext<global::Billing.Payload>(context, request), cancellationToken).ConfigureAwait(false);
        }

        private static async Task Invoke_Jobs_SourceGenerator_Tests_RootJob(IServiceProvider serviceProvider, global::Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken)
        {
            var request = await JobsRequestProvider.GetRequestAsync<global::Jobs.SourceGenerator.Tests.Payload>(context, cancellationToken).ConfigureAwait(false);
            var job = ActivatorUtilities.CreateInstance<global::Jobs.SourceGenerator.Tests.RootJob>(serviceProvider);
            await ((global::Headless.Jobs.Base.IJob<global::Jobs.SourceGenerator.Tests.Payload>)job).ExecuteAsync(new global::Headless.Jobs.Base.JobContext<global::Jobs.SourceGenerator.Tests.Payload>(context, request), cancellationToken).ConfigureAwait(false);
        }
    }
}
