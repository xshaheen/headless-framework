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

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("invoice.cleanup")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("invoice.create")]
namespace Jobs.SourceGenerator.Tests
{
    internal static class JobsInstanceFactoryExtensions
    {
        [global::System.Runtime.CompilerServices.ModuleInitializer]
        public static void Initialize()
        {
            var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>(2);
            jobFunctionDelegateDict.Add("invoice.create", new JobFunctionRegistration { CronExpression = "0 */5 * * * *", Priority = (JobPriority)1, Delegate = new JobFunctionDelegate(async (cancellationToken, serviceProvider, context) =>
            {
                var genericContext = await ToGenericContextWithRequest<Demo.CreateInvoice>(context, cancellationToken);
                await CreateDemoInvoiceJobs(serviceProvider).CreateAsync(genericContext, cancellationToken);
            }), MaxConcurrency = 3 });
            jobFunctionDelegateDict.Add("invoice.cleanup", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate((cancellationToken, serviceProvider, context) =>
            {
                CreateDemoInvoiceJobs(serviceProvider).Cleanup();
                return Task.CompletedTask;
            }), MaxConcurrency = 0 });
            JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, 2);
            RegisterRequestTypes();
            RegisterDescriptors();
        }

        private static void RegisterDescriptors()
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(2);
            descriptors.Add("invoice.create", new JobFunctionDescriptor("invoice.create", typeof(global::Demo.CreateInvoice), "0 */5 * * * *", (JobPriority)1, 3));
            descriptors.Add("invoice.cleanup", new JobFunctionDescriptor("invoice.cleanup", null, "", (JobPriority)0, 0));
            JobFunctionProvider.RegisterDescriptors(descriptors, 2);
        }

        private static Demo.InvoiceJobs CreateDemoInvoiceJobs(IServiceProvider serviceProvider)
        {
            return new Demo.InvoiceJobs();
        }

        private static async Task<JobFunctionContext<T>> ToGenericContextWithRequest<T>(JobFunctionContext context, CancellationToken cancellationToken)
        {
            var request = await JobsRequestProvider.GetRequestAsync<T>(context, cancellationToken);
            return new JobFunctionContext<T>(context, request);
        }

        private static void RegisterRequestTypes()
        {
            var requestTypes = new Dictionary<string, (string, Type)>(1);
            requestTypes.Add("invoice.create", (typeof(Demo.CreateInvoice).FullName, typeof(Demo.CreateInvoice)));
            JobFunctionProvider.RegisterRequestType(requestTypes, 1);
        }
    }
}