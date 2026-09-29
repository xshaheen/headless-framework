// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives;
using Headless.Generator.Primitives.Models;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Proves the generator is incremental: an edit that does not change a primitive reuses every tracked step, and no
/// step output holds a symbol, syntax node, or compilation, which would both pin memory and defeat value comparison.
/// </summary>
public sealed class IncrementalCachingTests : TestBase
{
    private const string _PrimitivesPath = "primitives.cs";
    private const string _UnrelatedPath = "unrelated.cs";

    private static readonly string[] _TrackedSteps =
    [
        "ParseResults",
        "Primitives",
        "AssemblyName",
        "GlobalOptions",
        "Diagnostics",
    ];

    // A numeric primitive carries SupportedOperations and a nested one carries ParentPrimitives: the two model members
    // that compared by reference before, so they are what an unrelated edit must not invalidate.
    private const string _PrimitivesSource = """
        using Headless.Generator.Primitives;

        namespace Demo;

        [SupportedOperations(Addition = true, Subtraction = true)]
        public readonly partial struct Amount : IPrimitive<decimal>
        {
            public static PrimitiveValidationResult Validate(decimal value) => PrimitiveValidationResult.Ok;
        }

        public readonly partial struct Discount : IPrimitive<Amount>
        {
            public static PrimitiveValidationResult Validate(Amount value) => PrimitiveValidationResult.Ok;
        }

        public sealed partial class Code : IPrimitive<string>
        {
            public static PrimitiveValidationResult Validate(string value) => PrimitiveValidationResult.Ok;
        }
        """;

    [Fact]
    public void should_reuse_every_tracked_step_when_an_unrelated_file_changes()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(compilation, _UnrelatedPath, "public sealed class Unrelated { public int Value; }");
        var result = _Run(driver, edited);

        IncrementalGeneratorAssertions.AssertStepsReused(result, _TrackedSteps);
        IncrementalGeneratorAssertions.AssertNoSymbolsOrSyntax(result, _TrackedSteps);
    }

    [Fact]
    public void should_reuse_every_tracked_step_when_an_unrelated_file_is_added()
    {
        var (driver, compilation) = _RunInitial();

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                "public sealed class Added : System.IDisposable { public void Dispose() { } }",
                GeneratorCompilation.ParseOptions,
                "added.cs",
                cancellationToken: AbortToken
            )
        );
        var result = _Run(driver, edited);

        IncrementalGeneratorAssertions.AssertStepsReused(result, _TrackedSteps);
    }

    [Fact]
    public void should_not_re_emit_source_when_an_edit_only_moves_primitives()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(compilation, _PrimitivesPath, "// moved\n\n\n" + _PrimitivesSource);
        var result = _Run(driver, edited);

        // Locations live beside the emission model, so moving code refreshes diagnostics but not generated source.
        IncrementalGeneratorAssertions
            .StepReasons(result, "Primitives")
            .Should()
            .OnlyContain(reason =>
                reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged
            );
        IncrementalGeneratorAssertions.AssertNoSymbolsOrSyntax(result, _TrackedSteps);
    }

    [Fact]
    public void should_regenerate_when_a_primitive_changes()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(
            compilation,
            _PrimitivesPath,
            _PrimitivesSource.Replace("Addition = true", "Addition = false", StringComparison.Ordinal)
        );
        var result = _Run(driver, edited);

        IncrementalGeneratorAssertions
            .StepReasons(result, "Primitives")
            .Should()
            .Contain(IncrementalStepRunReason.Modified);
        result
            .GeneratedSources.Single(source => string.Equals(source.HintName, "Amount.g.cs", StringComparison.Ordinal))
            .SourceText.ToString()
            .Should()
            .NotContain("IAdditionOperators");
    }

    [Fact]
    public void should_report_declaration_diagnostics_at_the_type_identifier()
    {
        const string source = """
            using Headless.Generator.Primitives;

            namespace Demo;

            public sealed partial class Count : IPrimitive<int>
            {
                public static PrimitiveValidationResult Validate(int value) => PrimitiveValidationResult.Ok;
            }

            public readonly struct Name : IPrimitive<string>
            {
                public static PrimitiveValidationResult Validate(string value) => PrimitiveValidationResult.Ok;
            }
            """;
        var compilation = GeneratorCompilation.Create(
            "Caching.Diagnostics",
            [(_PrimitivesPath, source)],
            TestHelpers.References.Value
        );

        var result = _CreateDriver().RunGenerators(compilation, AbortToken).GetRunResult().Results.Single();

        var tree = compilation.SyntaxTrees.Single();
        result
            .Diagnostics.Select(diagnostic =>
                (diagnostic.Id, Text: source[diagnostic.Location.SourceSpan.Start..diagnostic.Location.SourceSpan.End])
            )
            .Should()
            .BeEquivalentTo([("HF1015", "Count"), ("HF1016", "Name"), ("HF1002", "Name")]);
        // Anchoring to the compilation's own tree is what lets #pragma suppress these warnings.
        result.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Location.SourceTree == tree);
    }

    private static (GeneratorDriver Driver, CSharpCompilation Compilation) _RunInitial()
    {
        var compilation = GeneratorCompilation.Create(
            "Caching.Primitives",
            [(_PrimitivesPath, _PrimitivesSource), (_UnrelatedPath, "public sealed class Unrelated { }")],
            TestHelpers.References.Value
        );
        var driver = _CreateDriver().RunGenerators(compilation, AbortToken);

        var initial = driver.GetRunResult().Results.Single();
        initial.Exception.Should().BeNull();
        initial.Diagnostics.Should().BeEmpty();
        initial.GeneratedSources.Should().NotBeEmpty();
        IncrementalGeneratorAssertions.AssertNoSymbolsOrSyntax(initial, _TrackedSteps);
        return (driver, compilation);
    }

    private static GeneratorDriver _CreateDriver() =>
        GeneratorCompilation.CreateTrackingDriver(
            new PrimitiveGenerator(),
            new TestHelpers.PrimitiveConfigOptionsProvider(new PrimitiveGlobalOptions { GenerateJsonConverters = true })
        );

    private static GeneratorRunResult _Run(GeneratorDriver driver, Compilation compilation)
    {
        var result = driver.RunGenerators(compilation, AbortToken).GetRunResult().Results.Single();
        result.Exception.Should().BeNull();
        return result;
    }

    private static Compilation _ReplaceText(Compilation compilation, string path, string text)
    {
        var tree = compilation.SyntaxTrees.Single(tree => string.Equals(tree.FilePath, path, StringComparison.Ordinal));
        return compilation.ReplaceSyntaxTree(tree, tree.WithChangedText(SourceText.From(text)));
    }
}
