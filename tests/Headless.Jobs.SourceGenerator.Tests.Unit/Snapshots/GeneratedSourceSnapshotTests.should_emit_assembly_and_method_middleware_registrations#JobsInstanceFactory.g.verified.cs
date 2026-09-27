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
using Jobs.SourceGenerator.Tests;

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("middleware.local", "1")]
namespace Jobs.SourceGenerator.Tests
{
    internal static class JobsInstanceFactoryExtensions
    {
        [global::System.Runtime.CompilerServices.ModuleInitializer]
        public static void Initialize()
        {
            var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>(1);
            jobFunctionDelegateDict.Add("middleware.local", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate((serviceProvider, context, cancellationToken) =>
            {
                CreateDemoMiddlewareMiddlewareJobs(serviceProvider).Run();
                return Task.CompletedTask;
            }), MaxConcurrency = 0 });
            JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, 1);
            RegisterRequestTypes();
            RegisterDescriptors();
            JobMiddlewareRegistry.RegisterSchedule("Jobs.SourceGenerator.Tests:Demo.Middleware.GlobalSchedule", null, 5, static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Demo.Middleware.GlobalSchedule>().InvokeAsync(context, next, cancellationToken));
            JobMiddlewareRegistry.RegisterExecute("Jobs.SourceGenerator.Tests:Demo.Middleware.GlobalExecute", null, 0, static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Demo.Middleware.GlobalExecute>().InvokeAsync(context, next, cancellationToken));
            JobMiddlewareRegistry.RegisterExecute("Jobs.SourceGenerator.Tests:Demo.Middleware.LocalExecute", "middleware.local", 1, static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Demo.Middleware.LocalExecute>().InvokeAsync(context, next, cancellationToken));
            JobMiddlewareRegistry.RegisterExecute("Jobs.SourceGenerator.Tests:Demo.Middleware.GlobalExecute", "producer.run", -5, static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Demo.Middleware.GlobalExecute>().InvokeAsync(context, next, cancellationToken));
        }

        private static void RegisterDescriptors()
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(1);
            descriptors.Add("middleware.local", AppJobs.middleware_u002E_local);
            JobFunctionProvider.RegisterDescriptors(descriptors, 1);
        }

        private static global::Demo.Middleware.MiddlewareJobs CreateDemoMiddlewareMiddlewareJobs(IServiceProvider serviceProvider)
        {
            return new global::Demo.Middleware.MiddlewareJobs();
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
        public static JobFunctionDescriptor middleware_u002E_local { get; } = new JobFunctionDescriptor("middleware.local", null, "", (JobPriority)0, 0, "1");
    }
}