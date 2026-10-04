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

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("middleware.local", "1")]
namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var functions = new Dictionary<string, JobFunctionRegistration>(1);
            functions.Add("middleware.local", new JobFunctionRegistration { CronExpression = "", Priority = (JobPriority)0, Delegate = Invoke_Demo_Middleware_MiddlewareJob, MaxConcurrency = 0, JobType = typeof(global::Demo.Middleware.MiddlewareJob) });
            catalog.AddFunctions(functions);
            RegisterRequestTypes(catalog);
            RegisterDescriptors(catalog);
            catalog.AddScheduleMiddleware("Jobs.SourceGenerator.Tests:Demo.Middleware.GlobalSchedule", null, 5, static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Demo.Middleware.GlobalSchedule>().InvokeAsync(context, next, cancellationToken));
            catalog.AddExecuteMiddleware("Jobs.SourceGenerator.Tests:Demo.Middleware.GlobalExecute", null, 0, static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Demo.Middleware.GlobalExecute>().InvokeAsync(context, next, cancellationToken));
            catalog.AddExecuteMiddleware("Jobs.SourceGenerator.Tests:Demo.Middleware.LocalExecute", "middleware.local", 1, static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Demo.Middleware.LocalExecute>().InvokeAsync(context, next, cancellationToken));
            catalog.AddExecuteMiddleware("Jobs.SourceGenerator.Tests:Demo.Middleware.GlobalExecute", "producer.run", -5, static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Demo.Middleware.GlobalExecute>().InvokeAsync(context, next, cancellationToken));
        }

        private static void RegisterDescriptors(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(1);
            descriptors.Add("middleware.local", new JobFunctionDescriptor("middleware.local", null, "", (JobPriority)0, 0, "1"));
            catalog.AddDescriptors(descriptors);
        }

        private static void RegisterRequestTypes(global::Headless.Jobs.JobsCatalogBuilder catalog)
        {
        }

        private static async Task Invoke_Demo_Middleware_MiddlewareJob(IServiceProvider serviceProvider, global::Headless.Jobs.JobContext context, CancellationToken cancellationToken)
        {
            var job = ActivatorUtilities.CreateInstance<global::Demo.Middleware.MiddlewareJob>(serviceProvider);
            await ((global::Headless.Jobs.IJob)job).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
