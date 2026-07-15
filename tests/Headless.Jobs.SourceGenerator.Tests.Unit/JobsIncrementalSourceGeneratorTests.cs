// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Tests;

public sealed class JobsIncrementalSourceGeneratorTests
{
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

        diagnosticIds.Should().BeEquivalentTo(Enumerable.Range(1, 19).Select(number => $"HF{number:000}"));
    }

    [Fact]
    public Task should_generate_descriptors_for_typed_and_requestless_functions()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;
            using Headless.Jobs.Enums;

            namespace Demo;

            public sealed record CreateInvoice(string Number);

            public sealed class InvoiceJobs
            {
                [JobFunction("invoice.create", "0 */5 * * * *", JobPriority.High, 3)]
                public Task CreateAsync(JobFunctionContext<CreateInvoice> context, CancellationToken cancellationToken)
                    => Task.CompletedTask;

                [JobFunction("invoice.cleanup")]
                public void Cleanup() { }
            }
            """,
            out var compilationDiagnostics
        );

        compilationDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Verifier.Verify(driver).UseDirectory("Snapshots");
    }

    [Fact]
    public void should_report_duplicate_function_names()
    {
        var diagnostics = GeneratorTestHelper
            .Run(
                """
                using Headless.Jobs.Base;

                public sealed class Jobs
                {
                    [JobFunction("duplicate")]
                    public void First() { }

                    [JobFunction("duplicate")]
                    public void Second() { }
                }
                """
            )
            .GetRunResult()
            .Diagnostics;

        diagnostics.Should().Contain(diagnostic => string.Equals(diagnostic.Id, "HF005", StringComparison.Ordinal));
    }

    [Fact]
    public void should_report_duplicate_request_types()
    {
        var diagnostics = GeneratorTestHelper
            .Run(
                """
                using Headless.Jobs.Base;

                public sealed record Request(string Value);

                public sealed class Jobs
                {
                    [JobFunction("first")]
                    public void First(JobFunctionContext<Request> context) { }

                    [JobFunction("second")]
                    public void Second(JobFunctionContext<Request> context) { }
                }
                """
            )
            .GetRunResult()
            .Diagnostics;

        diagnostics
            .Should()
            .Contain(diagnostic =>
                string.Equals(diagnostic.Id, "HF011", StringComparison.Ordinal)
                && diagnostic.Severity == DiagnosticSeverity.Error
            );
    }

    [Fact]
    public void should_allow_multiple_requestless_functions()
    {
        var diagnostics = GeneratorTestHelper
            .Run(
                """
                using Headless.Jobs.Base;

                public sealed class Jobs
                {
                    [JobFunction("first")]
                    public void First() { }

                    [JobFunction("second")]
                    public void Second() { }
                }
                """
            )
            .GetRunResult()
            .Diagnostics;

        diagnostics.Should().NotContain(diagnostic => string.Equals(diagnostic.Id, "HF011", StringComparison.Ordinal));
    }

    [Fact]
    public void should_report_invalid_descriptor_metadata_before_emission()
    {
        var diagnostics = GeneratorTestHelper
            .Run(
                """
                using Headless.Jobs.Base;
                using Headless.Jobs.Enums;

                public sealed class Jobs
                {
                    [JobFunction("broken", (JobPriority)999, -1)]
                    public void Broken() { }
                }
                """
            )
            .GetRunResult()
            .Diagnostics;

        diagnostics
            .Should()
            .Contain(diagnostic =>
                string.Equals(diagnostic.Id, "HF012", StringComparison.Ordinal)
                && diagnostic.Severity == DiagnosticSeverity.Error
            );
        diagnostics
            .Should()
            .Contain(diagnostic =>
                string.Equals(diagnostic.Id, "HF013", StringComparison.Ordinal)
                && diagnostic.Severity == DiagnosticSeverity.Error
            );
    }

    [Fact]
    public void should_report_unknown_and_duplicate_middleware_declarations()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;
            using Headless.Jobs.Base;

            [assembly: JobScheduleMiddleware<ScheduleMiddleware>(Function = "missing")]
            [assembly: JobScheduleMiddleware<ScheduleMiddleware>]
            [assembly: JobScheduleMiddleware<ScheduleMiddleware>]

            public sealed class ScheduleMiddleware : IJobScheduleMiddleware
            {
                public Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            public sealed class Jobs { [JobFunction("known")] public void Run() { } }
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
    public void should_derive_method_target_and_reject_invalid_method_placement()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;
            using Headless.Jobs.Base;

            public sealed class ExecuteMiddleware : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            public sealed class ScheduleMiddleware : IJobScheduleMiddleware
            {
                public Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            public sealed class Handlers
            {
                [JobFunction("invoice.create")]
                [JobScheduleMiddleware<ScheduleMiddleware>]
                [JobExecuteMiddleware<ExecuteMiddleware>]
                public void Create() { }

                [JobExecuteMiddleware<ExecuteMiddleware>]
                public void MissingFunction() { }

                [JobFunction("invoice.other")]
                [JobExecuteMiddleware<ExecuteMiddleware>(Function = "invoice.create")]
                public void ExplicitLocalTarget() { }
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
                "JobMiddlewareRegistry.RegisterSchedule(\"Jobs.SourceGenerator.Tests:ScheduleMiddleware\", \"invoice.create\", 0"
            );
        generated
            .Should()
            .Contain(
                "JobMiddlewareRegistry.RegisterExecute(\"Jobs.SourceGenerator.Tests:ExecuteMiddleware\", \"invoice.create\", 0"
            );
        generated
            .Should()
            .NotContain(
                "JobMiddlewareRegistry.RegisterExecute(\"Jobs.SourceGenerator.Tests:ExecuteMiddleware\", \"invoice.other\""
            );
    }

    [Fact]
    public void should_validate_external_function_fallback_from_emitted_descriptor_metadata()
    {
        var producer = GeneratorTestHelper.EmitReference(
            "Producer.Jobs",
            """
            using Headless.Jobs.Base;

            public sealed class ProducerJobs
            {
                [JobFunction("producer.run")]
                public void Run() { }
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
                "JobMiddlewareRegistry.RegisterExecute(\"Jobs.SourceGenerator.Tests:ExternalMiddleware\", \"producer.run\", 0"
            );
    }

    [Fact]
    public void should_reject_assembly_fallback_to_a_local_function()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;
            using Headless.Jobs.Base;

            [assembly: JobExecuteMiddleware<ExecuteMiddleware>(Function = "local.run")]

            public sealed class ExecuteMiddleware : IJobExecuteMiddleware
            {
                public Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken) => next(cancellationToken);
            }

            public sealed class Handlers
            {
                [JobFunction("local.run")]
                public void Run() { }
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
    public void should_reject_inaccessible_method_local_middleware_without_emitting_broken_code()
    {
        var driver = GeneratorTestHelper.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs;
            using Headless.Jobs.Base;

            public sealed class Handlers
            {
                [JobFunction("private.run")]
                [JobExecuteMiddleware<PrivateMiddleware>]
                public void Run() { }

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

    private static string _GeneratedSource(GeneratorDriver driver) =>
        driver.GetRunResult().Results.Single().GeneratedSources.Single().SourceText.ToString();

    private static string[] _RegistrationLines(GeneratorDriver driver) =>
        _GeneratedSource(driver)
            .Split('\n')
            .Where(line => line.Contains("JobMiddlewareRegistry.Register", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();
}
