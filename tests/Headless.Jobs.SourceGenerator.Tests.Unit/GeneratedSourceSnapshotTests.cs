// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// Golden snapshots of the complete generated registration source, one per emit branch. They exist so a change to how
/// the generator builds its output cannot silently change what it emits: any difference shows up as a snapshot diff.
/// </summary>
public sealed class GeneratedSourceSnapshotTests
{
    [Fact]
    public Task should_emit_constructor_injection_for_every_constructor_shape()
    {
        return _VerifyGenerated(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo.Injection;

            public interface IClock;

            public interface IStore;

            public sealed class RegularConstructorJobs
            {
                public RegularConstructorJobs(
                    IClock clock,
                    [FromKeyedServices("primary")] IStore store,
                    IServiceProvider serviceProvider
                ) { }

                [JobFunction("injection.regular")]
                public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class PrimaryConstructorJobs(IClock clock, [FromKeyedServices(42)] IStore store)
            {
                [JobFunction("injection.primary")]
                public void Run(JobFunctionContext context) { }
            }

            public sealed class MarkedConstructorJobs
            {
                public MarkedConstructorJobs() { }

                [JobsConstructor]
                public MarkedConstructorJobs(IClock clock) { }

                [JobFunction("injection.marked")]
                public async Task RunAsync(JobFunctionContext context, CancellationToken cancellationToken) =>
                    await Task.Yield();
            }

            public static class StaticJobs
            {
                [JobFunction("injection.static", "0 0 * * * *")]
                public static Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """
        );
    }

    [Fact]
    public Task should_emit_fully_qualified_request_type_names()
    {
        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Jobs.SourceGenerator.Tests
            {
                public sealed record Payload(int Id);

                public sealed record Order(int Id);

                public sealed class RootJobs
                {
                    [JobFunction("root.instance")]
                    public Task RunAsync(JobFunctionContext<Payload> context, CancellationToken cancellationToken) =>
                        Task.CompletedTask;

                    [JobFunction("root.order")]
                    public Task OrderAsync(JobFunctionContext<Order> context) => Task.CompletedTask;

                    [JobFunction("root.static")]
                    public static void Run() { }
                }
            }

            namespace Billing
            {
                public sealed record Payload(string Id);

                public sealed class BillingJobs
                {
                    [JobFunction("billing.run", "%Jobs:Billing:Cron%")]
                    public Task RunAsync(JobFunctionContext<Payload> context) => Task.CompletedTask;
                }
            }
            """
        );
    }

    [Fact]
    public Task should_emit_descriptor_metadata_recovery_knobs_and_escaped_handles()
    {
        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;
            using Headless.Jobs.Enums;

            namespace Demo.Knobs;

            public sealed class KnobJobs
            {
                [JobFunction(
                    "knobs.all",
                    "*/5 * * * * *",
                    JobPriority.LongRunning,
                    4,
                    ContractVersion = "v7",
                    OnMissedRun = MissedRunPolicy.Skip,
                    MissedRunGraceSeconds = 90,
                    OnOverlap = CronOverlapPolicy.Skip
                )]
                public Task AllAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                [JobFunction("class", JobPriority.Low)]
                public void Keyword() { }

                [JobFunction("ToString")]
                public void ObjectMember() { }

                [JobFunction("1start")]
                public void Digit() { }
            }
            """
        );
    }

    [Fact]
    public Task should_emit_assembly_and_method_middleware_registrations()
    {
        var reference = GeneratorTestHelper.EmitReference(
            "Snapshot.Producer",
            """
            using Headless.Jobs.Base;

            namespace Snapshot.Producer;

            public sealed class ProducerJobs
            {
                [JobFunction("producer.run")]
                public void Run() { }
            }
            """,
            out var referenceDiagnostics
        );
        referenceDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;
            using Headless.Jobs.Base;

            [assembly: JobScheduleMiddleware<Demo.Middleware.GlobalSchedule>(Priority = 5)]
            [assembly: JobExecuteMiddleware<Demo.Middleware.GlobalExecute>]
            [assembly: JobExecuteMiddleware<Demo.Middleware.GlobalExecute>(Function = "producer.run", Priority = -5)]

            namespace Demo.Middleware;

            public sealed class GlobalSchedule : IJobScheduleMiddleware
            {
                public Task InvokeAsync(JobScheduleContext c, JobScheduleNext n, CancellationToken t) => n(t);
            }

            public sealed class GlobalExecute : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext c, JobExecuteNext n, CancellationToken t) => n(t);
            }

            public sealed class LocalExecute : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext c, JobExecuteNext n, CancellationToken t) => n(t);
            }

            public sealed class MiddlewareJobs
            {
                [JobFunction("middleware.local")]
                [JobExecuteMiddleware<LocalExecute>(Priority = 1)]
                public void Run() { }
            }
            """,
            reference
        );
    }

    [Fact]
    public Task should_emit_global_namespace_functions()
    {
        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            public sealed class GlobalJobs
            {
                [JobFunction("global.run")]
                public Task RunAsync(JobFunctionContext context, CancellationToken cancellationToken) =>
                    Task.CompletedTask;
            }
            """
        );
    }

    [Fact]
    public void should_emit_no_module_when_no_function_or_middleware_is_declared()
    {
        var driver = GeneratorTestHelper.Run(
            """
            namespace Demo.Empty;

            public sealed class NotAJob
            {
                public void Run() { }
            }
            """,
            out var diagnostics
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_emit_a_module_the_host_adds_and_no_module_initializer()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using Headless.Jobs.Base;

            namespace Demo;

            public sealed class Jobs
            {
                [JobFunction("module.run")]
                public void Run() { }
            }

            public static class Host
            {
                public static void Configure(Headless.Jobs.JobsOptionsBuilder<Headless.Jobs.Entities.TimeJobEntity, Headless.Jobs.Entities.CronJobEntity> jobs) =>
                    jobs.AddModule<global::Jobs.SourceGenerator.Tests.JobsModule>();
            }
            """,
            out var diagnostics
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = driver.GetRunResult().GeneratedTrees.Single().ToString();
        generated.Should().Contain("public sealed class JobsModule : global::Headless.Jobs.IJobsModule");
        generated.Should().Contain("static void global::Headless.Jobs.IJobsModule.Register()");
        generated.Should().NotContain("ModuleInitializer");
    }

    private static Task _VerifyGenerated(string source, params MetadataReference[] references)
    {
        var driver = GeneratorTestHelper.Run(source, out var diagnostics, references);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Verify(driver).UseDirectory("Snapshots");
    }
}
