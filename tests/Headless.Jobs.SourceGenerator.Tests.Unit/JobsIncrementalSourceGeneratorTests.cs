// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

        diagnosticIds.Should().BeEquivalentTo(Enumerable.Range(1, 13).Select(number => $"HF{number:000}"));
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
}
