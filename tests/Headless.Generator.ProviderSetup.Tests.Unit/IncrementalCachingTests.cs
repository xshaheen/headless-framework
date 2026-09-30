// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.ProviderSetup;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Tests;

/// <summary>
/// Proves the generator is incremental: an unrelated edit reuses every tracked step, moving a declaration does not
/// re-emit source, and no step output holds a symbol, syntax node, or compilation.
/// </summary>
public sealed class IncrementalCachingTests : TestBase
{
    private const string _ProviderPath = "provider.cs";
    private const string _UnrelatedPath = "unrelated.cs";

    private static readonly string[] _TrackedSteps = ["ProviderOptions", "GenerationResult", "Models", "Diagnostics"];

    [Fact]
    public void should_reuse_every_tracked_step_when_an_unrelated_file_changes()
    {
        var (driver, compilation) = _RunInitial();

        var result = _Run(
            driver,
            _ReplaceText(compilation, _UnrelatedPath, "public sealed class Unrelated { public int Value; }")
        );

        IncrementalGeneratorAssertions.AssertStepsReused(result, _TrackedSteps);
        IncrementalGeneratorAssertions.AssertNoSymbolsOrSyntax(result, _TrackedSteps);
    }

    [Fact]
    public void should_not_re_emit_source_when_an_edit_only_moves_the_options_class()
    {
        var (driver, compilation) = _RunInitial();

        var result = _Run(
            driver,
            _ReplaceText(compilation, _ProviderPath, "// moved\n\n\n" + GeneratorTestHelper.SmsProviderSource)
        );

        // Locations live beside the models, never in them, so moving code refreshes diagnostics only.
        IncrementalGeneratorAssertions
            .StepReasons(result, "Models")
            .Should()
            .OnlyContain(reason =>
                reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged
            );
        IncrementalGeneratorAssertions.AssertNoSymbolsOrSyntax(result, _TrackedSteps);
    }

    [Fact]
    public void should_regenerate_when_the_declared_effect_changes()
    {
        var (driver, compilation) = _RunInitial();

        var edited = GeneratorTestHelper.SmsProviderSource.Replace(
            "[OutboundEffect(OutboundEffect.Unsafe)]",
            "[OutboundEffect(OutboundEffect.Safe)]",
            StringComparison.Ordinal
        );
        var result = _Run(driver, _ReplaceText(compilation, _ProviderPath, edited));

        IncrementalGeneratorAssertions
            .StepReasons(result, "Models")
            .Should()
            .Contain(IncrementalStepRunReason.Modified);
        result.GeneratedSources.Single().SourceText.ToString().Should().Contain("OutboundEffect.Safe");
    }

    private (GeneratorDriver Driver, Compilation Compilation) _RunInitial()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(
            "Acme.Sms",
            [
                (_ProviderPath, GeneratorTestHelper.SmsProviderSource),
                (_UnrelatedPath, "public sealed class Unrelated { }"),
            ]
        );

        var driver = GeneratorCompilation
            .CreateTrackingDriver(new ProviderSetupGenerator())
            .RunGenerators(compilation, AbortToken);

        return (driver, compilation);
    }

    private GeneratorRunResult _Run(GeneratorDriver driver, Compilation compilation)
    {
        return driver.RunGenerators(compilation, AbortToken).GetRunResult().Results.Single();
    }

    private Compilation _ReplaceText(Compilation compilation, string path, string text)
    {
        var tree = compilation.SyntaxTrees.Single(t => string.Equals(t.FilePath, path, StringComparison.Ordinal));

        return compilation.ReplaceSyntaxTree(tree, tree.WithChangedText(SourceText.From(text)).WithFilePath(path));
    }
}
