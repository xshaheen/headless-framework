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
    public void should_compile_a_void_function_that_takes_a_typed_context()
    {
        var (diagnostics, generated) = _Generate(
            "Demo",
            """
            using Headless.Jobs.Base;

            namespace Demo;

            public sealed record Payload(int Id);

            public sealed class Jobs
            {
                [JobFunction("typed.void")]
                public void Run(JobFunctionContext<Payload> context) { }
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        generated.Should().NotContain("return Task.CompletedTask;");
    }

    [Fact]
    public void should_compile_when_a_namespace_segment_matches_the_assembly_name_suffix()
    {
        // Generated code lives in `namespace Billing.Producer`, where an unqualified `Producer.X` binds to the
        // enclosing `Billing.Producer` namespace instead of the top-level `Producer`.
        var (diagnostics, _) = _Generate(
            "Billing.Producer",
            """
            using System;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Producer;

            public interface IClock;

            public sealed record Payload(int Id);

            public sealed class Jobs(IClock clock)
            {
                [JobFunction("producer.run")]
                public Task RunAsync(JobFunctionContext<Payload> context) => Task.CompletedTask;

                [JobFunction("producer.static")]
                public static void Static() { }
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void should_compile_request_types_whose_simple_names_collide()
    {
        var (diagnostics, generated) = _Generate(
            "Demo",
            """
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Billing
            {
                public sealed record Payload(int Id);

                public sealed class BillingJobs
                {
                    [JobFunction("billing.run")]
                    public Task RunAsync(JobFunctionContext<Payload> context) => Task.CompletedTask;
                }
            }

            namespace Shipping
            {
                public sealed record Payload(int Id);

                public sealed record Order(int Id);

                public sealed class ShippingJobs
                {
                    [JobFunction("shipping.run")]
                    public Task RunAsync(JobFunctionContext<Payload> context) => Task.CompletedTask;

                    [JobFunction("shipping.order")]
                    public Task OrderAsync(JobFunctionContext<Order> context) => Task.CompletedTask;
                }
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        generated.Should().Contain("ToGenericContextWithRequest<global::Shipping.Order>");
    }

    [Fact]
    public void should_compile_classes_declared_in_nested_namespace_blocks()
    {
        var (diagnostics, generated) = _Generate(
            "Demo",
            """
            using Headless.Jobs.Base;

            namespace Outer
            {
                namespace Inner
                {
                    public sealed class Jobs
                    {
                        [JobFunction("nested.namespace")]
                        public void Run() { }
                    }
                }
            }
            """
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        generated.Should().Contain("new global::Outer.Inner.Jobs()");
    }

    [Fact]
    public void should_construct_a_partial_class_once_from_the_constructor_in_any_part()
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

                    public sealed partial class Jobs
                    {
                        [JobFunction("partial.first")]
                        public void First() { }
                    }
                    """
                ),
                (
                    "second.cs",
                    """
                    using Headless.Jobs.Base;

                    namespace Demo;

                    partial class Jobs
                    {
                        public Jobs(IClock clock) { }

                        [JobFunction("partial.second")]
                        public void Second() { }
                    }
                    """
                ),
            ]
        );

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        diagnostics.Should().NotContain(diagnostic => string.Equals(diagnostic.Id, "HF001", StringComparison.Ordinal));
        generated.Split("private static global::Demo.Jobs CreateDemoJobs(").Should().HaveCount(2);
        generated.Should().Contain("serviceProvider.GetService<global::Demo.IClock>()");
    }

    [Fact]
    public void should_escape_function_names_and_configuration_cron_keys()
    {
        var (diagnostics, _) = _Generate(
            "Demo",
            """
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace Demo;

            public sealed record Payload(int Id);

            public sealed class Jobs
            {
                [JobFunction("quote\"and\\slash", "%Jobs:\"Quoted\"%")]
                public Task RunAsync(JobFunctionContext<Payload> context) => Task.CompletedTask;
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
            using Headless.Jobs.Base;

            namespace Demo;

            sealed class ImplicitlyInternal
            {
                [JobFunction("implicit.internal")]
                public void Run() { }
            }

            file sealed class FileLocal
            {
                [JobFunction("file.local")]
                public void Run() { }
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
