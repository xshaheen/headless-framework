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

[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("injection.marked", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("injection.primary", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("injection.regular", "1")]
[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute("injection.static", "1")]
namespace Jobs.SourceGenerator.Tests
{
    /// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c> inside <c>AddHeadlessJobs</c>.</summary>
    public sealed class JobsModule : global::Headless.Jobs.IJobsModule
    {
        private JobsModule() { }

        static void global::Headless.Jobs.IJobsModule.Register()
        {
            var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>(4);
            jobFunctionDelegateDict.Add("injection.regular", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate(async (serviceProvider, context, cancellationToken) =>
            {
                await CreateDemoInjectionRegularConstructorJobs(serviceProvider).RunAsync(cancellationToken);
            }), MaxConcurrency = 0 });
            jobFunctionDelegateDict.Add("injection.primary", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate((serviceProvider, context, cancellationToken) =>
            {
                CreateDemoInjectionPrimaryConstructorJobs(serviceProvider).Run(context);
                return Task.CompletedTask;
            }), MaxConcurrency = 0 });
            jobFunctionDelegateDict.Add("injection.marked", new JobFunctionRegistration { CronExpression = string.Empty, Priority = (JobPriority)0, Delegate = new JobFunctionDelegate(async (serviceProvider, context, cancellationToken) =>
            {
                await CreateDemoInjectionMarkedConstructorJobs(serviceProvider).RunAsync(context, cancellationToken);
            }), MaxConcurrency = 0 });
            jobFunctionDelegateDict.Add("injection.static", new JobFunctionRegistration { CronExpression = "0 0 * * * *", Priority = (JobPriority)0, Delegate = new JobFunctionDelegate(async (serviceProvider, context, cancellationToken) =>
            {
                await global::Demo.Injection.StaticJobs.RunAsync(cancellationToken);
            }), MaxConcurrency = 0 });
            JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, 4);
            RegisterRequestTypes();
            RegisterDescriptors();
        }

        private static void RegisterDescriptors()
        {
            var descriptors = new Dictionary<string, JobFunctionDescriptor>(4);
            descriptors.Add("injection.marked", AppJobs.injection_u002E_marked);
            descriptors.Add("injection.primary", AppJobs.injection_u002E_primary);
            descriptors.Add("injection.regular", AppJobs.injection_u002E_regular);
            descriptors.Add("injection.static", AppJobs.injection_u002E_static);
            JobFunctionProvider.RegisterDescriptors(descriptors, 4);
        }

        private static global::Demo.Injection.RegularConstructorJobs CreateDemoInjectionRegularConstructorJobs(IServiceProvider serviceProvider)
        {
            var clock = serviceProvider.GetService<global::Demo.Injection.IClock>();
            var store = serviceProvider.GetKeyedService<global::Demo.Injection.IStore>("primary");
            return new global::Demo.Injection.RegularConstructorJobs(clock, store, serviceProvider);
        }

        private static global::Demo.Injection.PrimaryConstructorJobs CreateDemoInjectionPrimaryConstructorJobs(IServiceProvider serviceProvider)
        {
            var clock = serviceProvider.GetService<global::Demo.Injection.IClock>();
            var store = serviceProvider.GetKeyedService<global::Demo.Injection.IStore>(42);
            return new global::Demo.Injection.PrimaryConstructorJobs(clock, store);
        }

        private static global::Demo.Injection.MarkedConstructorJobs CreateDemoInjectionMarkedConstructorJobs(IServiceProvider serviceProvider)
        {
            var clock = serviceProvider.GetService<global::Demo.Injection.IClock>();
            return new global::Demo.Injection.MarkedConstructorJobs(clock);
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
        public static JobFunctionDescriptor injection_u002E_marked { get; } = new JobFunctionDescriptor("injection.marked", null, "", (JobPriority)0, 0, "1");
        /// <summary>A canonical requestless job descriptor.</summary>
        public static JobFunctionDescriptor injection_u002E_primary { get; } = new JobFunctionDescriptor("injection.primary", null, "", (JobPriority)0, 0, "1");
        /// <summary>A canonical requestless job descriptor.</summary>
        public static JobFunctionDescriptor injection_u002E_regular { get; } = new JobFunctionDescriptor("injection.regular", null, "", (JobPriority)0, 0, "1");
        /// <summary>A canonical requestless job descriptor.</summary>
        public static JobFunctionDescriptor injection_u002E_static { get; } = new JobFunctionDescriptor("injection.static", null, "0 0 * * * *", (JobPriority)0, 0, "1");
    }
}