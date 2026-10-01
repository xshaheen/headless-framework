// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tests;

/// <summary>
/// Declarations the generator must turn into code that compiles. Each case produced a compiler error in the generated
/// file before the generator emitted fully qualified names, escaped literals, and read classes from their symbols.
/// </summary>
public sealed class GeneratedSourceCompilationTests
{
    [Fact]
    public void should_compile_when_a_namespace_segment_matches_the_assembly_name_suffix()
    {
        // Generated code lives in `namespace Billing.Producer`, where an unqualified `Producer.X` binds to the
        // enclosing `Billing.Producer` namespace instead of the top-level `Producer`.
        var (diagnostics, _) = _Generate(
            "Billing.Producer",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Producer;

            public interface IClock;

            public sealed record Payload(int Id);

            [Job("producer.run")]
            public sealed class RunJob(IClock clock) : IJob<Payload>
            {
                public ValueTask ExecuteAsync(JobContext<Payload> context, CancellationToken cancellationToken) =>
                    default;
            }

            [Job("producer.plain")]
            public sealed class PlainJob : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void should_compile_argument_types_whose_simple_names_collide()
    {
        var (diagnostics, generated) = _Generate(
            "Demo",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Billing
            {
                public sealed record Payload(int Id);

                [Job("billing.run")]
                public sealed class BillingJob : IJob<Payload>
                {
                    public ValueTask ExecuteAsync(JobContext<Payload> context, CancellationToken cancellationToken) =>
                        default;
                }
            }

            namespace Shipping
            {
                public sealed record Payload(int Id);

                [Job("shipping.run")]
                public sealed class ShippingJob : IJob<Payload>
                {
                    public ValueTask ExecuteAsync(JobContext<Payload> context, CancellationToken cancellationToken) =>
                        default;
                }
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        generated.Should().Contain("JobsRequestProvider.GetRequestAsync<global::Shipping.Payload>");
        generated.Should().Contain("JobsRequestProvider.GetRequestAsync<global::Billing.Payload>");
    }

    [Fact]
    public void should_compile_classes_declared_in_nested_namespace_blocks()
    {
        var (diagnostics, generated) = _Generate(
            "Demo",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Outer
            {
                namespace Inner
                {
                    [Job("nested.namespace")]
                    public sealed class NestedNamespaceJob : IJob
                    {
                        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
                            default;
                    }
                }
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        generated.Should().Contain("ActivatorUtilities.CreateInstance<global::Outer.Inner.NestedNamespaceJob>");
    }

    [Fact]
    public void should_compile_a_partial_job_declared_across_files()
    {
        var (diagnostics, generated) = _Generate(
            "Demo",
            [
                (
                    "first.cs",
                    """
                    using Headless.Jobs.Base;

                    namespace Demo;

                    public interface IClock;

                    [Job("partial.run")]
                    public sealed partial class PartialJob;
                    """
                ),
                (
                    "second.cs",
                    """
                    using System.Threading;
                    using System.Threading.Tasks;
                    using Headless.Jobs.Base;

                    namespace Demo;

                    partial class PartialJob : IJob
                    {
                        public PartialJob(IClock clock) { }

                        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
                            default;
                    }
                    """
                ),
            ]
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        generated.Split("private static async Task Invoke_Demo_PartialJob(").Should().HaveCount(2);
    }

    [Fact]
    public void should_escape_identities_and_configuration_cron_keys()
    {
        var (diagnostics, _) = _Generate(
            "Demo",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Demo;

            public sealed record Payload(int Id);

            [Job("quote.\"and\\slash", Cron = "%Jobs:\"Quoted\"%", TimeZone = "Zone\"Quoted")]
            public sealed class EscapedJob : IJob<Payload>
            {
                public ValueTask ExecuteAsync(JobContext<Payload> context, CancellationToken cancellationToken) =>
                    default;
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void should_accept_an_implicitly_internal_class_and_reject_a_file_local_class()
    {
        var (diagnostics, _) = _Generate(
            "Demo",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Demo;

            [Job("implicit.internal")]
            sealed class ImplicitlyInternal : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }

            [Job("file.local")]
            file sealed class FileLocal : IJob
            {
                public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
            }
            """
        );

        diagnostics
            .Where(diagnostic => string.Equals(diagnostic.Id, "HF001", StringComparison.Ordinal))
            .Should()
            .ContainSingle()
            .Which.GetMessage(CultureInfo.InvariantCulture)
            .Should()
            .Contain("FileLocal");
    }

    [Fact]
    public void should_compile_a_record_class_job_and_a_job_whose_namespace_declares_its_own_job_context()
    {
        // The generated file imports the assembly's namespace, so a user type named JobContext must not capture the
        // invoker's parameter type.
        var (diagnostics, _) = _Generate(
            "Demo",
            """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class JobContext;

            [Headless.Jobs.Base.Job("record.run")]
            public sealed record RecordJob : Headless.Jobs.Base.IJob
            {
                public ValueTask ExecuteAsync(Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken) =>
                    default;
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) _Generate(
        string assemblyName,
        string source
    ) => _Generate(assemblyName, [("jobs.cs", source)]);

    private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) _Generate(
        string assemblyName,
        IReadOnlyCollection<(string Path, string Source)> sources
    )
    {
        var compilation = GeneratorTestHelper.CreateCompilation(assemblyName, sources, []);
        CSharpGeneratorDriver
            .Create(
                [new JobsIncrementalSourceGenerator().AsSourceGenerator()],
                parseOptions: GeneratorCompilation.ParseOptions
            )
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var generated = string.Concat(
            output.SyntaxTrees.Except(compilation.SyntaxTrees).Select(tree => tree.ToString())
        );
        return (output.GetDiagnostics().AddRange(generatorDiagnostics), generated);
    }
}
