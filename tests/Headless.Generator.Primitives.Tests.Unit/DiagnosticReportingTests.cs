// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Proves each reported rule lands on the construct that caused it, and that a rejected attribute is dropped rather
/// than emitted into code that does not compile.
/// </summary>
public sealed class DiagnosticReportingTests : TestBase
{
    [Fact]
    public void should_report_unsupported_underlying_type_at_the_type_name()
    {
        const string source = """
            using System;
            using Headless.Generator.Primitives;

            namespace Demo;

            public readonly partial struct Stamp : IPrimitive<Uri>
            {
                public static PrimitiveValidationResult Validate(Uri value) => PrimitiveValidationResult.Ok;
            }
            """;

        var (diagnostics, generated, _) = _Run(source);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("HF1001");
        _Text(source, diagnostics[0]).Should().Be("Stamp");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).Should().Contain("System.Uri");
        generated.Should().BeEmpty();
    }

    [Fact]
    public void should_report_serialization_format_on_a_non_date_primitive_at_the_attribute()
    {
        const string source = """
            using Headless.Generator.Primitives;

            namespace Demo;

            [SerializationFormat("N2")]
            public readonly partial struct Quantity : IPrimitive<int>
            {
                public static PrimitiveValidationResult Validate(int value) => PrimitiveValidationResult.Ok;
            }
            """;

        var (diagnostics, generated, compilationErrors) = _Run(source);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("HF1012");
        _Text(source, diagnostics[0]).Should().Be("SerializationFormat(\"N2\")");
        // The format is dropped, so the generated primitive still compiles instead of calling int.ParseExact.
        generated.Should().NotBeEmpty();
        compilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void should_accept_serialization_format_on_a_date_primitive()
    {
        const string source = """
            using System;
            using Headless.Generator.Primitives;

            namespace Demo;

            [SerializationFormat("yyyy-MM-dd")]
            public readonly partial struct BirthDate : IPrimitive<DateOnly>
            {
                public static PrimitiveValidationResult Validate(DateOnly value) => PrimitiveValidationResult.Ok;
            }
            """;

        var (diagnostics, generated, compilationErrors) = _Run(source);

        diagnostics.Should().BeEmpty();
        generated.Should().Contain(text => text.Contains("yyyy-MM-dd", StringComparison.Ordinal));
        compilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void should_report_supported_operations_on_a_non_numeric_primitive_at_the_attribute()
    {
        const string source = """
            using Headless.Generator.Primitives;

            namespace Demo;

            [SupportedOperations(Addition = true)]
            public sealed partial class Code : IPrimitive<string>
            {
                public static PrimitiveValidationResult Validate(string value) => PrimitiveValidationResult.Ok;
            }
            """;

        var (diagnostics, generated, compilationErrors) = _Run(source);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("HF1013");
        _Text(source, diagnostics[0]).Should().Be("SupportedOperations(Addition = true)");
        generated.Should().NotBeEmpty();
        compilationErrors.Should().BeEmpty();
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string[] Generated, Diagnostic[] CompilationErrors) _Run(
        string source
    )
    {
        var compilation = GeneratorCompilation.Create(
            "Diagnostics.Primitives",
            [("primitives.cs", source)],
            TestHelpers.References.Value
        );

        CSharpGeneratorDriver
            .Create(new PrimitiveGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, AbortToken);

        var generated = output.SyntaxTrees.Skip(1).Select(tree => tree.ToString()).ToArray();
        var errors = output
            .GetDiagnostics(AbortToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        return (diagnostics, generated, errors);
    }

    private static string _Text(string source, Diagnostic diagnostic) =>
        source[diagnostic.Location.SourceSpan.Start..diagnostic.Location.SourceSpan.End];
}
