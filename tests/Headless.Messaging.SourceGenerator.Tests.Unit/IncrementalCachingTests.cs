// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.SourceGenerator;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Tests;

/// <summary>
/// Proves the generator is incremental: an edit that does not change a declaration reuses every tracked step, and no
/// step output holds a symbol, syntax node, or compilation, which would both pin memory and defeat value comparison.
/// </summary>
public sealed class IncrementalCachingTests : TestBase
{
    private const string _ConsumersPath = "consumers.cs";
    private const string _UnrelatedPath = "unrelated.cs";

    private static readonly string[] _TrackedSteps =
    [
        "BusConsumers",
        "QueueConsumers",
        "AssemblyName",
        "GenerationResult",
        "RegistrationModel",
        "Diagnostics",
        "RequestCalls",
        "LocalResponders",
        "ReferencedResponders",
        "RequestDiagnostics",
    ];

    // The Queue consumer comes first, so an edit to the Bus consumer does not move it and its result stays equal.
    private const string _ConsumersSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Messaging;

        namespace Billing;

        public sealed record InvoiceIssued(string Number);
        public sealed record InvoicePaid(string Number);
        public sealed record IssueInvoiceCommand(string OrderId);

        [QueueConsumer("billing.issue-invoice")]
        public sealed class IssueInvoice : IConsume<IssueInvoiceCommand>
        {
            public ValueTask ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken) => default;
        }

        [BusConsumer("billing.invoice-projection")]
        public sealed class InvoiceProjection : IConsume<InvoiceIssued>, IConsume<InvoicePaid>
        {
            public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) => default;
            public ValueTask ConsumeAsync(ConsumeContext<InvoicePaid> context, CancellationToken cancellationToken) => default;
        }

        // Exercises the request-call steps, so they are tracked and must be reused too.
        public sealed class InvoiceLookup(IRequestClient requests)
        {
            public Task<InvoiceIssued> FindAsync(InvoicePaid paid) => requests.RequestAsync<InvoicePaid, InvoiceIssued>(paid);
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
                "public sealed class Added { }",
                GeneratorCompilation.ParseOptions,
                "added.cs",
                cancellationToken: AbortToken
            )
        );
        var result = _Run(driver, edited);

        IncrementalGeneratorAssertions.AssertStepsReused(result, _TrackedSteps);
    }

    [Fact]
    public void should_not_re_emit_source_when_an_edit_only_moves_consumers()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(compilation, _ConsumersPath, "// moved\n\n\n" + _ConsumersSource);
        var result = _Run(driver, edited);

        // Locations live beside the emission model, so moving code refreshes diagnostics but not generated source.
        IncrementalGeneratorAssertions
            .StepReasons(result, "RegistrationModel")
            .Should()
            .OnlyContain(reason =>
                reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged
            );
        IncrementalGeneratorAssertions.AssertNoSymbolsOrSyntax(result, _TrackedSteps);
    }

    [Fact]
    public void should_regenerate_when_a_consumer_changes()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(
            compilation,
            _ConsumersPath,
            _ConsumersSource.Replace(
                "[BusConsumer(\"billing.invoice-projection\")]",
                "[BusConsumer(\"billing.invoice-projection\", EveryInstance = true)]",
                StringComparison.Ordinal
            )
        );
        var result = _Run(driver, edited);

        IncrementalGeneratorAssertions
            .StepReasons(result, "BusConsumers")
            .Should()
            .Contain(IncrementalStepRunReason.Modified);
        IncrementalGeneratorAssertions
            .StepReasons(result, "QueueConsumers")
            .Should()
            .OnlyContain(reason =>
                reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged
            );
        IncrementalGeneratorAssertions
            .StepReasons(result, "RegistrationModel")
            .Should()
            .Contain(IncrementalStepRunReason.Modified);
        result.GeneratedSources.Single().SourceText.ToString().Should().Contain("everyInstance: true");
    }

    private static (GeneratorDriver Driver, CSharpCompilation Compilation) _RunInitial()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(
            "Caching.Consumers",
            [(_ConsumersPath, _ConsumersSource), (_UnrelatedPath, "public sealed class Unrelated { }")]
        );
        var driver = GeneratorCompilation
            .CreateTrackingDriver(new MessagingIncrementalSourceGenerator())
            .RunGenerators(compilation);

        var initial = driver.GetRunResult().Results.Single();
        initial.Exception.Should().BeNull();
        initial.Diagnostics.Should().BeEmpty();
        initial.GeneratedSources.Should().ContainSingle();
        IncrementalGeneratorAssertions.AssertNoSymbolsOrSyntax(initial, _TrackedSteps);
        return (driver, compilation);
    }

    private static GeneratorRunResult _Run(GeneratorDriver driver, Compilation compilation)
    {
        var result = driver.RunGenerators(compilation).GetRunResult().Results.Single();
        result.Exception.Should().BeNull();
        return result;
    }

    private static CSharpCompilation _ReplaceText(CSharpCompilation compilation, string path, string text)
    {
        var tree = compilation.SyntaxTrees.Single(x => string.Equals(x.FilePath, path, StringComparison.Ordinal));
        return compilation.ReplaceSyntaxTree(tree, tree.WithChangedText(SourceText.From(text)));
    }
}
