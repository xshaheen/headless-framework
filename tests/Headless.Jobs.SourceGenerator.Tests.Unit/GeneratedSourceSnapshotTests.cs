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
    public Task should_emit_a_typed_invoker_for_every_job_shape()
    {
        return _VerifyGenerated(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo.Shapes;

            public interface IClock;

            public sealed record InvoiceArgs(int Id);

            [Job("shapes.plain")]
            public sealed class CloseDay(IClock clock) : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }

            [Job("shapes.typed")]
            public sealed class SendInvoice : IJob<InvoiceArgs>
            {
                public ValueTask ExecuteAsync(JobContext<InvoiceArgs> context, CancellationToken cancellationToken) =>
                    default;
            }

            [Job("shapes.explicit")]
            internal sealed class ExplicitJob : IJob
            {
                ValueTask IJob.ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }

            [Job("shapes.disposable")]
            public sealed class DisposableJob : IJob, IDisposable
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;

                public void Dispose() { }
            }

            [Job("shapes.async-disposable")]
            public sealed class AsyncDisposableJob : IJob, IAsyncDisposable
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;

                public ValueTask DisposeAsync() => default;
            }
            """
        );
    }

    [Fact]
    public Task should_emit_fully_qualified_argument_type_names()
    {
        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            namespace Jobs.SourceGenerator.Tests
            {
                public sealed record Payload(int Id);

                [Job("root.payload")]
                public sealed class RootJob : IJob<Payload>
                {
                    public ValueTask ExecuteAsync(JobContext<Payload> context, CancellationToken cancellationToken) =>
                        default;
                }
            }

            namespace Billing
            {
                public sealed record Payload(string Id);

                [Job("billing.run", Cron = "%Jobs:Billing:Cron")]
                public sealed class BillingJob : IJob<Payload>
                {
                    public ValueTask ExecuteAsync(JobContext<Payload> context, CancellationToken cancellationToken) =>
                        default;
                }
            }
            """
        );
    }

    [Fact]
    public Task should_emit_descriptor_metadata_and_every_attribute_knob()
    {
        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            namespace Demo.Knobs;

            [Job(
                "knobs.all",
                Cron = "*/5 * * * * *",
                TimeZone = "Africa/Cairo",
                Priority = JobPriority.LongRunning,
                MaxConcurrency = 4,
                ContractVersion = "v7",
                OnMissedRun = MissedRunPolicy.Skip,
                MissedRunGraceSeconds = 90,
                OnOverlap = CronOverlapPolicy.Skip
            )]
            public sealed class AllKnobs : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }

            [Job("knobs.defaults")]
            public sealed class Defaults : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """
        );
    }

    [Fact]
    public Task should_emit_assembly_and_class_middleware_registrations()
    {
        var reference = GeneratorTestHelper.EmitReference(
            "Snapshot.Producer",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            namespace Snapshot.Producer;

            [Job("producer.run")]
            public sealed class ProducerJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
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

            [Job("middleware.local")]
            [JobExecuteMiddleware<LocalExecute>(Priority = 1)]
            public sealed class MiddlewareJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """,
            reference
        );
    }

    [Fact]
    public Task should_emit_global_namespace_jobs()
    {
        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [Job("global.run")]
            public sealed class GlobalJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """
        );
    }

    [Fact]
    public void should_emit_no_module_when_no_job_or_middleware_is_declared()
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
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            namespace Demo;

            [Job("module.run")]
            public sealed class ModuleJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }

            public static class Host
            {
                public static void Configure(Headless.Jobs.JobsOptionsBuilder<Headless.Jobs.TimeJobEntity, Headless.Jobs.CronJobEntity> jobs) =>
                    jobs.AddModule<global::Jobs.SourceGenerator.Tests.JobsModule>();
            }
            """,
            out var diagnostics
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = driver.GetRunResult().GeneratedTrees.Single().ToString();
        generated.Should().Contain("public sealed class JobsModule : global::Headless.Jobs.IJobsModule");
        generated
            .Should()
            .Contain(
                "static void global::Headless.Jobs.IJobsModule.Register(global::Headless.Jobs.JobsCatalogBuilder catalog)"
            );
        generated.Should().NotContain("ModuleInitializer");
        generated.Should().NotContain("AppJobs");
    }

    private static Task _VerifyGenerated(string source, params MetadataReference[] references)
    {
        var driver = GeneratorTestHelper.Run(source, out var diagnostics, references);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Verify(driver).UseDirectory("Snapshots");
    }
}
