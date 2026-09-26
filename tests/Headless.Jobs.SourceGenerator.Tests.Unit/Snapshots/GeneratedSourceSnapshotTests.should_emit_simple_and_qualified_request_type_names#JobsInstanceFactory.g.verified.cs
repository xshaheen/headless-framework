//HintName: JobsInstanceFactory.g.cs
//Jobs readonly auto-generated file.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Base;
using Jobs.SourceGenerator.Tests;

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("billing.run", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("root.instance", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("root.order", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("root.static", "1")]
namespace Jobs.SourceGenerator.Tests
{
    internal static class JobsInstanceFactoryExtensions
    {
        [global::System.Runtime.CompilerServices.ModuleInitializer]
        public static void Initialize()
        {
            var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>(4);
            jobFunctionDelegateDict.Add("root.instance", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate(async (serviceProvider, context, cancellationToken) =>
            {
                var genericContext = await ToGenericContextWithRequest<Jobs.SourceGenerator.Tests.Payload>(context, cancellationToken);
                await CreateJobsSourceGeneratorTestsRootJobs(serviceProvider).RunAsync(genericContext, cancellationToken);
            }), MaxConcurrency = 0 });
            jobFunctionDelegateDict.Add("root.order", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate(async (serviceProvider, context, cancellationToken) =>
            {
                var genericContext = await ToGenericContextWithRequest<Order>(context, cancellationToken);
                await CreateJobsSourceGeneratorTestsRootJobs(serviceProvider).OrderAsync(genericContext);
            }), MaxConcurrency = 0 });
            jobFunctionDelegateDict.Add("root.static", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate((serviceProvider, context, cancellationToken) =>
            {
                RootJobs.Run();
                return Task.CompletedTask;
            }), MaxConcurrency = 0 });
            jobFunctionDelegateDict.Add("billing.run", new JobFunctionRegistration { CronExpression = "%Jobs:Billing:Cron%", Priority = (JobPriority)0, Delegate = new JobFunctionDelegate(async (serviceProvider, context, cancellationToken) =>
            {
                var genericContext = await ToGenericContextWithRequest<Billing.Payload>(context, cancellationToken);
                await CreateBillingBillingJobs(serviceProvider).RunAsync(genericContext);
            }), MaxConcurrency = 0 });
            JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, 4);
            RegisterRequestTypes();
            RegisterDescriptors();
        }

        private static void RegisterDescriptors()
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(4);
            descriptors.Add("billing.run", new JobFunctionDescriptor("billing.run", typeof(global::Billing.Payload), "%Jobs:Billing:Cron%", (JobPriority)0, 0, "1"));
            descriptors.Add("root.instance", new JobFunctionDescriptor("root.instance", typeof(global::Jobs.SourceGenerator.Tests.Payload), "", (JobPriority)0, 0, "1"));
            descriptors.Add("root.order", new JobFunctionDescriptor("root.order", typeof(global::Jobs.SourceGenerator.Tests.Order), "", (JobPriority)0, 0, "1"));
            descriptors.Add("root.static", AppJobs.root_u002E_static);
            JobFunctionProvider.RegisterDescriptors(descriptors, 4);
        }

        private static RootJobs CreateJobsSourceGeneratorTestsRootJobs(IServiceProvider serviceProvider)
        {
            return new RootJobs();
        }

        private static Billing.BillingJobs CreateBillingBillingJobs(IServiceProvider serviceProvider)
        {
            return new Billing.BillingJobs();
        }

        private static async Task<JobFunctionContext<T>> ToGenericContextWithRequest<T>(JobFunctionContext context, CancellationToken cancellationToken)
        {
            var request = await JobsRequestProvider.GetRequestAsync<T>(context, cancellationToken);
            return new JobFunctionContext<T>(context, request);
        }

        private static void RegisterRequestTypes()
        {
            var requestTypes = new Dictionary<string, (string, Type)>(3);
            requestTypes.Add("root.instance", (typeof(Jobs.SourceGenerator.Tests.Payload).FullName, typeof(Jobs.SourceGenerator.Tests.Payload)));
            requestTypes.Add("root.order", (typeof(Order).FullName, typeof(Order)));
            requestTypes.Add("billing.run", (typeof(Billing.Payload).FullName, typeof(Billing.Payload)));
            JobFunctionProvider.RegisterRequestType(requestTypes, 3);
        }
    }
}

namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Canonical generated handles for this assembly's requestless jobs.</summary>
    public static class AppJobs
    {
        /// <summary>A canonical requestless job descriptor.</summary>
        public static JobFunctionDescriptor root_u002E_static { get; } = new JobFunctionDescriptor("root.static", null, "", (JobPriority)0, 0, "1");
    }
}