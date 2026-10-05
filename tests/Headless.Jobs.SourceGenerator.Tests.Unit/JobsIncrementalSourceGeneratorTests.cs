// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tests;

public sealed class JobsIncrementalSourceGeneratorTests
{
    [Fact]
    public void explicit_contract_metadata_is_stable_across_source_reference_order_and_clr_rename()
    {
        const string usings = "using System.Threading; using System.Threading.Tasks; using Headless.Jobs; ";
        const string first =
            usings
            + "[Job(\"stable.contract\", ContractVersion = \"schema-v2\")] public sealed class OldName : IJob { public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default; }";
        const string renamed =
            usings
            + "[Job(\"stable.contract\", ContractVersion = \"schema-v2\")] public sealed class NewName : IJob { public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default; }";
        const string other =
            usings
            + "[Job(\"other.contract\", ContractVersion = \"v3\")] public sealed class Other : IJob { public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default; }";
        var referenceA = GeneratorTestHelper.EmitReference(
            "ContractReferenceA",
            "public sealed class ReferenceA { }",
            out var diagnosticsA
        );
        var referenceB = GeneratorTestHelper.EmitReference(
            "ContractReferenceB",
            "public sealed class ReferenceB { }",
            out var diagnosticsB
        );
        diagnosticsA.Concat(diagnosticsB).Should().NotContain(x => x.Severity == DiagnosticSeverity.Error);
        var forward = GeneratorTestHelper.Run(
            [("first.cs", first), ("other.cs", other)],
            out var forwardDiagnostics,
            referenceA,
            referenceB
        );
        var reverse = GeneratorTestHelper.Run(
            [("other.cs", other), ("first.cs", first)],
            out var reverseDiagnostics,
            referenceB,
            referenceA
        );
        var rename = GeneratorTestHelper.Run(
            [("first.cs", renamed), ("other.cs", other)],
            out var renameDiagnostics,
            referenceA,
            referenceB
        );
        forwardDiagnostics
            .Concat(reverseDiagnostics)
            .Concat(renameDiagnostics)
            .Should()
            .NotContain(x => x.Severity == DiagnosticSeverity.Error);

        static string[] Contracts(GeneratorDriver driver) =>
            [
                .. driver
                    .GetRunResult()
                    .GeneratedTrees.SelectMany(tree => tree.ToString().Split('\n'))
                    .Where(line =>
                        line.Contains("descriptors.Add(", StringComparison.Ordinal)
                        || line.Contains("JobFunctionDescriptorMetadataAttribute(", StringComparison.Ordinal)
                    ),
            ];
        Contracts(forward).Should().Equal(Contracts(reverse));
        Contracts(forward).Should().Equal(Contracts(rename));
        Contracts(forward).Should().Contain(line => line.Contains("\"schema-v2\"", StringComparison.Ordinal));
    }

    [Fact]
    public void should_use_the_headless_framework_diagnostic_prefix_for_every_descriptor()
    {
        var descriptorsType = typeof(Headless.Jobs.SourceGenerator.JobsIncrementalSourceGenerator).Assembly.GetType(
            "Headless.Jobs.SourceGenerator.Validation.DiagnosticDescriptors"
        );

        descriptorsType.Should().NotBeNull();
        var diagnosticIds = descriptorsType!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null))
            .OfType<DiagnosticDescriptor>()
            .Select(descriptor => descriptor.Id);

        // HF002, HF006, and HF010 governed job methods and constructor selection, which no longer exist.
        diagnosticIds
            .Should()
            .BeEquivalentTo(
                Enumerable
                    .Range(1, 23)
                    .Where(number => number is not (2 or 6 or 10))
                    .Select(number => $"HF{number:000}")
            );
    }

    [Fact]
    public Task should_generate_descriptors_for_jobs_with_and_without_arguments()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            namespace Demo;

            public sealed record CreateInvoice(string Number);

            [Job(
                "invoice.create",
                Cron = "0 */5 * * * *",
                Priority = JobPriority.High,
                MaxConcurrency = 3,
                ContractVersion = "schema-v2"
            )]
            public sealed class CreateInvoiceJob : IJob<CreateInvoice>
            {
                public ValueTask ExecuteAsync(JobContext<CreateInvoice> context, CancellationToken cancellationToken) =>
                    default;
            }

            [Job("invoice.cleanup")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """,
            out var compilationDiagnostics
        );

        compilationDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Verify(driver).UseDirectory("Snapshots");
    }

    [Fact]
    public void should_generate_a_module_entry_and_an_invoker_for_a_job()
    {
        var driver = GeneratorTestHelper.Run(
            _Usings
                + """
                [Job("billing.close-day")]
                public sealed class CloseDay : IJob
                {
                    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
                }
                """,
            out var diagnostics
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = _GeneratedSource(driver);
        generated.Should().Contain("functions.Add(\"billing.close-day\", new JobFunctionRegistration {");
        generated.Should().Contain("Delegate = Invoke_Jobs_SourceGenerator_Tests_CloseDay");
        generated.Should().Contain("JobType = typeof(global::Jobs.SourceGenerator.Tests.CloseDay)");
        generated
            .Should()
            .Contain("ActivatorUtilities.CreateInstance<global::Jobs.SourceGenerator.Tests.CloseDay>(serviceProvider)");
    }

    [Fact]
    public void should_report_a_job_class_that_implements_no_job_interface_at_the_attribute()
    {
        const string source = """
            [Job("billing.close-day")]
            public sealed class CloseDay
            {
            }
            """;
        var driver = GeneratorTestHelper.Run(_Usings + source, out _);

        var diagnostic = driver.GetRunResult().Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("HF009");
        diagnostic
            .Location.SourceTree!.ToString()[diagnostic.Location.SourceSpan.Start..diagnostic.Location.SourceSpan.End]
            .Should()
            .Be("Job(\"billing.close-day\")");
        _GeneratedSource(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_report_a_job_class_that_implements_more_than_one_job_interface()
    {
        var diagnostics = _Diagnostics(
            """
            public sealed record First;
            public sealed record Second;

            [Job("billing.ambiguous")]
            public sealed class Ambiguous : IJob, IJob<First>, IJob<Second>
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
                public ValueTask ExecuteAsync(JobContext<First> context, CancellationToken cancellationToken) => default;
                public ValueTask ExecuteAsync(JobContext<Second> context, CancellationToken cancellationToken) => default;
            }
            """
        );

        diagnostics.Select(diagnostic => diagnostic.Id).Should().Equal("HF009");
    }

    [Fact]
    public void should_report_duplicate_job_identities()
    {
        var diagnostics = _Diagnostics(_Job("billing.duplicate", "First") + "\n" + _Job("billing.duplicate", "Second"));

        diagnostics.Select(diagnostic => diagnostic.Id).Should().Equal("HF005");
    }

    [Fact]
    public void should_report_two_jobs_that_take_the_same_argument_type()
    {
        var driver = GeneratorTestHelper.Run(
            _Usings
                + """
                public sealed record InvoiceArgs(int Id);

                [Job("billing.send-invoice")]
                public sealed class SendInvoice : IJob<InvoiceArgs>
                {
                    public ValueTask ExecuteAsync(JobContext<InvoiceArgs> context, CancellationToken cancellationToken) =>
                        default;
                }

                [Job("billing.resend-invoice")]
                public sealed class ResendInvoice : IJob<InvoiceArgs>
                {
                    public ValueTask ExecuteAsync(JobContext<InvoiceArgs> context, CancellationToken cancellationToken) =>
                        default;
                }
                """,
            out _
        );

        var diagnostic = driver.GetRunResult().Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("HF011");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("InvoiceArgs");
        _GeneratedSource(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_allow_several_jobs_without_arguments()
    {
        var diagnostics = _Diagnostics(_Job("billing.first", "First") + "\n" + _Job("billing.second", "Second"));

        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("closeDay")]
    [InlineData("")]
    [InlineData(" billing.close-day")]
    [InlineData("billing.close-day ")]
    [InlineData(".close-day")]
    [InlineData("billing.")]
    [InlineData("billing..close-day")]
    [InlineData("billing.close\u0001day")]
    public void should_report_an_identity_that_is_not_in_owner_name_form(string identity)
    {
        var diagnostics = _Diagnostics(_Job(identity, "CloseDay"));

        diagnostics.Select(diagnostic => diagnostic.Id).Should().Equal("HF004");
    }

    [Fact]
    public void should_report_an_identity_longer_than_the_storage_limit()
    {
        var diagnostics = _Diagnostics(_Job("billing." + new string('x', 193), "CloseDay"));

        diagnostics.Select(diagnostic => diagnostic.Id).Should().Equal("HF004");
    }

    [Theory]
    [InlineData("billing.close-day")]
    [InlineData("billing.reports.close-day")]
    [InlineData("b.c")]
    public void should_accept_an_identity_in_owner_name_form(string identity)
    {
        _Diagnostics(_Job(identity, "CloseDay")).Should().BeEmpty();
    }

    [Fact]
    public void should_accept_an_identity_at_the_storage_limit()
    {
        _Diagnostics(_Job("billing." + new string('x', 192), "CloseDay")).Should().BeEmpty();
    }

    [Fact]
    public void should_report_a_null_identity()
    {
        var diagnostics = _Diagnostics(
            """
            [Job(null!)]
            public sealed class CloseDay : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """
        );

        diagnostics.Select(diagnostic => diagnostic.Id).Should().Equal("HF004");
    }

    [Fact]
    public void should_report_an_invalid_literal_cron_expression()
    {
        var diagnostics = _Diagnostics(_Job("billing.close-day", "CloseDay", ", Cron = \"not a cron\""));

        diagnostics.Select(diagnostic => diagnostic.Id).Should().Equal("HF003");
    }

    [Theory]
    [InlineData("%Jobs:Daily")]
    [InlineData("%Jobs:Daily%")]
    public void should_defer_a_configuration_cron_to_startup(string cron)
    {
        var driver = GeneratorTestHelper.Run(
            _Usings + _Job("billing.close-day", "CloseDay", $", Cron = \"{cron}\""),
            out var diagnostics
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        _GeneratedSource(driver).Should().Contain($"CronExpression = \"{cron}\"");
    }

    [Fact]
    public void should_record_a_valid_time_zone_on_the_registration()
    {
        var driver = GeneratorTestHelper.Run(
            _Usings + _Job("billing.close-day", "CloseDay", ", Cron = \"0 0 0 * * *\", TimeZone = \"Africa/Cairo\""),
            out var diagnostics
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = _GeneratedSource(driver);
        generated.Should().Contain("TimeZoneId = \"Africa/Cairo\"");
    }

    [Theory]
    [InlineData("public abstract class CloseDay : IJob", "HF007")]
    [InlineData("public sealed class CloseDay<T> : IJob", "HF007")]
    [InlineData("file sealed class CloseDay : IJob", "HF001")]
    public void should_report_a_job_class_that_generated_code_cannot_construct(string declaration, string id)
    {
        var diagnostics = _Diagnostics(
            $$"""
            [Job("billing.close-day")]
            {{declaration}}
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """
        );

        diagnostics.Select(diagnostic => diagnostic.Id).Should().Equal(id);
    }

    [Fact]
    public void should_report_a_nested_job_class()
    {
        var diagnostics = _Diagnostics(
            """
            public static class Outer
            {
                [Job("billing.close-day")]
                public sealed class CloseDay : IJob
                {
                    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
                }
            }
            """
        );

        diagnostics.Select(diagnostic => diagnostic.Id).Should().Equal("HF008");
    }

    [Fact]
    public void should_report_invalid_descriptor_metadata_before_emission()
    {
        var diagnostics = _Diagnostics(
            _Job("billing.broken", "Broken", ", Priority = (Headless.Jobs.JobPriority)999, MaxConcurrency = -1")
        );

        diagnostics.Select(diagnostic => diagnostic.Id).Should().BeEquivalentTo("HF012", "HF013");
        diagnostics.Should().OnlyContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void should_report_a_negative_cluster_concurrency_before_emission()
    {
        var diagnostics = _Diagnostics(_Job("billing.broken", "Broken", ", ClusterMaxConcurrency = -1"));

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("HF013");
    }

    [Fact]
    public void should_report_unknown_and_duplicate_middleware_declarations()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [assembly: JobScheduleMiddleware<ScheduleMiddleware>(Function = "missing")]
            [assembly: JobScheduleMiddleware<ScheduleMiddleware>]
            [assembly: JobScheduleMiddleware<ScheduleMiddleware>]

            public sealed class ScheduleMiddleware : IJobScheduleMiddleware
            {
                public Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            [Job("test.known")] public sealed class KnownJob : IJob { public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default; }
            """,
            out _
        );
        var result = driver.GetRunResult();
        var diagnostics = result.Diagnostics;

        diagnostics
            .Where(diagnostic => string.Equals(diagnostic.Id, "HF014", StringComparison.Ordinal))
            .Should()
            .ContainSingle();
        diagnostics
            .Where(diagnostic => string.Equals(diagnostic.Id, "HF015", StringComparison.Ordinal))
            .Should()
            .ContainSingle();
        diagnostics
            .Where(diagnostic =>
                string.Equals(diagnostic.Id, "HF014", StringComparison.Ordinal)
                || string.Equals(diagnostic.Id, "HF015", StringComparison.Ordinal)
            )
            .Should()
            .OnlyContain(diagnostic => diagnostic.Location.SourceTree != null);

        var generated = _GeneratedSource(driver);
        generated.Should().NotContain("\"missing\"");
        _RegistrationLines(driver).Should().ContainSingle();
    }

    [Fact]
    public void should_emit_direct_deterministic_middleware_dispatch()
    {
        var sources = new (string Path, string Source)[]
        {
            (
                "zeta.cs",
                """
                using System.Threading;
                using System.Threading.Tasks;
                using Headless.Jobs;

                [assembly: JobScheduleMiddleware<Zeta>]
                [assembly: JobScheduleMiddleware<First>(Priority = -10)]

                public sealed class Zeta : IJobScheduleMiddleware { public Task InvokeAsync(JobScheduleContext c, JobScheduleNext n, CancellationToken t) => n(t); }
                public sealed class First : IJobScheduleMiddleware { public Task InvokeAsync(JobScheduleContext c, JobScheduleNext n, CancellationToken t) => n(t); }
                """
            ),
            (
                "alpha.cs",
                """
                using System.Threading;
                using System.Threading.Tasks;
                using Headless.Jobs;

                [assembly: JobScheduleMiddleware<Alpha>]
                [assembly: JobScheduleMiddleware<Last>(Priority = 10)]
                [assembly: JobExecuteMiddleware<ExecuteLast>(Priority = 20)]

                public sealed class Alpha : IJobScheduleMiddleware { public Task InvokeAsync(JobScheduleContext c, JobScheduleNext n, CancellationToken t) => n(t); }
                public sealed class Last : IJobScheduleMiddleware { public Task InvokeAsync(JobScheduleContext c, JobScheduleNext n, CancellationToken t) => n(t); }
                public sealed class ExecuteLast : IJobExecuteMiddleware { public Task InvokeAsync(JobExecuteContext c, JobExecuteNext n, CancellationToken t) => n(t); }
                """
            ),
        };

        var forward = GeneratorTestHelper.Run(sources, out var forwardDiagnostics);
        var reversed = GeneratorTestHelper.Run(sources.AsEnumerable().Reverse().ToArray(), out var reversedDiagnostics);

        forwardDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        reversedDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var forwardRegistrations = _RegistrationLines(forward);
        forwardRegistrations.Should().Equal(_RegistrationLines(reversed));
        forwardRegistrations
            .Select(line => line.Split(':')[1].Split('"')[0])
            .Should()
            .Equal("First", "Alpha", "Zeta", "Last", "ExecuteLast");

        var generated = _GeneratedSource(forward);
        generated
            .Should()
            .Contain(
                "static (context, next, cancellationToken) => context.Services.GetRequiredService<global::Alpha>().InvokeAsync(context, next, cancellationToken)"
            );
        generated.Should().NotContain("Assembly.Load");
        generated.Should().NotContain("Expression.Compile");
        generated.Should().NotContain("MethodInfo");
        generated.Should().NotContain("DynamicInvoke");
        generated.Should().NotContain("Activator.");
    }

    [Fact]
    public void should_preserve_interface_and_closed_generic_middleware_identities()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [assembly: JobScheduleMiddleware<IJobScheduleMiddleware>]
            [assembly: JobScheduleMiddleware<GenericMiddleware<First>>]
            [assembly: JobScheduleMiddleware<GenericMiddleware<Second>>]

            public sealed record First;
            public sealed record Second;

            public sealed class GenericMiddleware<T> : IJobScheduleMiddleware
            {
                public Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }
            """,
            out var compilationDiagnostics
        );

        compilationDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var registrations = _RegistrationLines(driver);
        registrations.Should().HaveCount(3);
        registrations
            .Should()
            .Contain(line => line.Contains(":Headless.Jobs.IJobScheduleMiddleware\"", StringComparison.Ordinal));
        registrations
            .Should()
            .Contain(line =>
                line.Contains("GenericMiddleware`1[Jobs.SourceGenerator.Tests:First]", StringComparison.Ordinal)
            );
        registrations
            .Should()
            .Contain(line =>
                line.Contains("GenericMiddleware`1[Jobs.SourceGenerator.Tests:Second]", StringComparison.Ordinal)
            );
    }

    [Fact]
    public void should_enforce_stage_specific_middleware_constraints()
    {
        GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [assembly: JobScheduleMiddleware<ExecuteOnly>]
            [assembly: JobExecuteMiddleware<ScheduleOnly>]

            public sealed class ExecuteOnly : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            public sealed class ScheduleOnly : IJobScheduleMiddleware
            {
                public Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }
            """,
            out var compilationDiagnostics
        );

        compilationDiagnostics
            .Where(diagnostic => string.Equals(diagnostic.Id, "CS0311", StringComparison.Ordinal))
            .Should()
            .HaveCount(2);
        compilationDiagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .OnlyContain(diagnostic => string.Equals(diagnostic.Id, "CS0311", StringComparison.Ordinal));
    }

    [Fact]
    public void should_derive_class_target_and_reject_invalid_class_placement()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            public sealed class ExecuteMiddleware : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            public sealed class ScheduleMiddleware : IJobScheduleMiddleware
            {
                public Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            [Job("invoice.create")]
            [JobScheduleMiddleware<ScheduleMiddleware>]
            [JobExecuteMiddleware<ExecuteMiddleware>]
            public sealed class Create : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }

            [JobExecuteMiddleware<ExecuteMiddleware>]
            public sealed class MissingJob;

            [Job("invoice.other")]
            [JobExecuteMiddleware<ExecuteMiddleware>(Function = "invoice.create")]
            public sealed class ExplicitLocalTarget : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """,
            out var compilationDiagnostics
        );
        compilationDiagnostics
            .Where(diagnostic =>
                !string.Equals(diagnostic.Id, "HF016", StringComparison.Ordinal)
                && !string.Equals(diagnostic.Id, "HF017", StringComparison.Ordinal)
            )
            .Should()
            .NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var diagnostics = driver.GetRunResult();

        diagnostics
            .Diagnostics.Where(diagnostic => string.Equals(diagnostic.Id, "HF016", StringComparison.Ordinal))
            .Should()
            .ContainSingle();
        diagnostics
            .Diagnostics.Where(diagnostic => string.Equals(diagnostic.Id, "HF017", StringComparison.Ordinal))
            .Should()
            .ContainSingle();
        var generated = _GeneratedSource(driver);
        generated
            .Should()
            .Contain(
                "catalog.AddScheduleMiddleware(\"Jobs.SourceGenerator.Tests:ScheduleMiddleware\", \"invoice.create\", 0"
            );
        generated
            .Should()
            .Contain(
                "catalog.AddExecuteMiddleware(\"Jobs.SourceGenerator.Tests:ExecuteMiddleware\", \"invoice.create\", 0"
            );
        generated
            .Should()
            .NotContain(
                "catalog.AddExecuteMiddleware(\"Jobs.SourceGenerator.Tests:ExecuteMiddleware\", \"invoice.other\""
            );
    }

    [Fact]
    public void should_validate_external_function_fallback_from_emitted_descriptor_metadata()
    {
        var producer = GeneratorTestHelper.EmitReference(
            "Producer.Jobs",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [Job("producer.run")]
            public sealed class ProducerJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """,
            out var producerDiagnostics
        );
        producerDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [assembly: JobExecuteMiddleware<ExternalMiddleware>(Function = "producer.run")]

            public sealed class ExternalMiddleware : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }
            """,
            out var consumerDiagnostics,
            producer
        );

        consumerDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        driver
            .GetRunResult()
            .Results.Single()
            .GeneratedSources.Single()
            .SourceText.ToString()
            .Should()
            .Contain(
                "catalog.AddExecuteMiddleware(\"Jobs.SourceGenerator.Tests:ExternalMiddleware\", \"producer.run\", 0"
            );
    }

    [Fact]
    public void should_reject_assembly_fallback_to_a_local_job()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [assembly: JobExecuteMiddleware<ExecuteMiddleware>(Function = "local.run")]

            public sealed class ExecuteMiddleware : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            [Job("local.run")]
            public sealed class LocalJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """,
            out _
        );
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics
            .Where(diagnostic => string.Equals(diagnostic.Id, "HF018", StringComparison.Ordinal))
            .Should()
            .ContainSingle();
        _RegistrationLines(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_reject_inaccessible_class_local_middleware_without_emitting_broken_code()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [Job("private.run")]
            [JobExecuteMiddleware<PrivateJob.PrivateMiddleware>]
            public sealed class PrivateJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;

                private sealed class PrivateMiddleware : IJobExecuteMiddleware
                {
                    public Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken) => next(cancellationToken);
                }
            }
            """,
            out var compilationDiagnostics
        );

        driver
            .GetRunResult()
            .Diagnostics.Where(diagnostic => string.Equals(diagnostic.Id, "HF019", StringComparison.Ordinal))
            .Should()
            .ContainSingle();
        compilationDiagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .OnlyContain(diagnostic => string.Equals(diagnostic.Id, "HF019", StringComparison.Ordinal));
        _RegistrationLines(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_report_duplicate_middleware_at_the_same_stable_source_location()
    {
        var sources = new (string Path, string Source)[]
        {
            (
                "a-first.cs",
                """
                using System.Threading;
                using System.Threading.Tasks;
                using Headless.Jobs;

                [assembly: JobScheduleMiddleware<ScheduleMiddleware>]

                public sealed class ScheduleMiddleware : IJobScheduleMiddleware
                {
                    public Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken) => next(cancellationToken);
                }
                """
            ),
            (
                "z-duplicate.cs",
                """
                using Headless.Jobs;

                [assembly: JobScheduleMiddleware<ScheduleMiddleware>]
                """
            ),
        };

        var forward = GeneratorTestHelper.Run(sources, out _).GetRunResult();
        var reversed = GeneratorTestHelper.Run(sources.AsEnumerable().Reverse().ToArray(), out _).GetRunResult();
        var forwardDuplicate = forward.Diagnostics.Single(diagnostic =>
            string.Equals(diagnostic.Id, "HF015", StringComparison.Ordinal)
        );
        var reversedDuplicate = reversed.Diagnostics.Single(diagnostic =>
            string.Equals(diagnostic.Id, "HF015", StringComparison.Ordinal)
        );

        forwardDuplicate.Location.SourceTree!.FilePath.Should().Be("z-duplicate.cs");
        reversedDuplicate.Location.SourceTree!.FilePath.Should().Be(forwardDuplicate.Location.SourceTree.FilePath);
        forwardDuplicate
            .GetMessage(CultureInfo.InvariantCulture)
            .Should()
            .Be(reversedDuplicate.GetMessage(CultureInfo.InvariantCulture));
        _RegistrationLines(GeneratorTestHelper.Run(sources, out _)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void should_reject_empty_external_function_fallback(string function)
    {
        var driver = GeneratorTestHelper.Run(
            $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;

            [assembly: JobExecuteMiddleware<ExecuteMiddleware>(Function = "{{function}}")]

            public sealed class ExecuteMiddleware : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }
            """,
            out _
        );

        driver
            .GetRunResult()
            .Diagnostics.Where(diagnostic => string.Equals(diagnostic.Id, "HF014", StringComparison.Ordinal))
            .Should()
            .ContainSingle();
        _RegistrationLines(driver).Should().BeEmpty();
    }

    private const string _Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Jobs;

        namespace Jobs.SourceGenerator.Tests;

        """;

    private static string _Job(string identity, string className, string extra = "") =>
        $$"""
            [Job({{SymbolDisplay.FormatLiteral(identity, quote: true)}}{{extra}})]
            public sealed class {{className}} : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """;

    private static ImmutableArray<Diagnostic> _Diagnostics(string source) =>
        GeneratorTestHelper.Run(_Usings + source).GetRunResult().Diagnostics;

    private static string _GeneratedSource(GeneratorDriver driver) =>
        driver.GetRunResult().Results.Single().GeneratedSources.SingleOrDefault().SourceText?.ToString()
        ?? string.Empty;

    private static string[] _RegistrationLines(GeneratorDriver driver)
    {
        return
        [
            .. _GeneratedSource(driver)
                .Split('\n')
                .Where(line =>
                    line.Contains("Middleware(", StringComparison.Ordinal)
                    && line.Contains("catalog.Add", StringComparison.Ordinal)
                )
                .Select(line => line.Trim()),
        ];
    }
}
