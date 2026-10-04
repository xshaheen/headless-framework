// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator;
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
    private const string _JobsPath = "jobs.cs";
    private const string _UnrelatedPath = "unrelated.cs";

    private static readonly string[] _TrackedSteps =
    [
        "Jobs",
        "ScheduleMiddleware",
        "ExecuteMiddleware",
        "AssemblyName",
        "ReferencedFunctions",
        "GenerationResult",
        "RegistrationModel",
        "Diagnostics",
    ];

    private const string _JobsSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Jobs;

        [assembly: JobExecuteMiddleware<Demo.AuditMiddleware>(Function = "producer.run")]
        [assembly: JobScheduleMiddleware<Demo.TraceMiddleware>]

        namespace Demo;

        public sealed record Invoice(string Number);

        public sealed class AuditMiddleware : IJobExecuteMiddleware
        {
            public Task InvokeAsync(JobExecuteContext c, JobExecuteNext n, CancellationToken t) => n(t);
        }

        public sealed class TraceMiddleware : IJobScheduleMiddleware
        {
            public Task InvokeAsync(JobScheduleContext c, JobScheduleNext n, CancellationToken t) => n(t);
        }

        [Job("invoice.send", Cron = "0 */5 * * * *")]
        [JobExecuteMiddleware<AuditMiddleware>]
        public sealed class SendInvoice : IJob<Invoice>
        {
            public ValueTask ExecuteAsync(JobContext<Invoice> context, CancellationToken cancellationToken) => default;
        }

        [Job("invoice.cleanup")]
        public sealed class Cleanup : IJob
        {
            public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
        }
        """;

    private const string _ProducerSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Jobs;

        namespace Upstream;

        [Job("producer.run")]
        public sealed class ProducerJob : IJob
        {
            public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
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
    public void should_not_re_emit_source_when_an_edit_only_moves_jobs()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(compilation, _JobsPath, "// moved\n\n\n" + _JobsSource);
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
    public void should_regenerate_when_a_job_changes()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(
            compilation,
            _JobsPath,
            _JobsSource.Replace("0 */5 * * * *", "0 */10 * * * *", StringComparison.Ordinal)
        );
        var result = _Run(driver, edited);

        IncrementalGeneratorAssertions.StepReasons(result, "Jobs").Should().Contain(IncrementalStepRunReason.Modified);
        IncrementalGeneratorAssertions
            .StepReasons(result, "RegistrationModel")
            .Should()
            .Contain(IncrementalStepRunReason.Modified);
        result.GeneratedSources.Single().SourceText.ToString().Should().Contain("0 */10 * * * *");
    }

    [Fact]
    public void should_revalidate_middleware_targets_when_a_referenced_job_is_renamed()
    {
        var (driver, compilation) = _RunInitial();
        var renamedProducer = GeneratorTestHelper.EmitReference(
            "CachingProducer",
            _ProducerSource.Replace("producer.run", "producer.renamed", StringComparison.Ordinal),
            out _
        );

        var edited = compilation.RemoveReferences(compilation.References.Last()).AddReferences(renamedProducer);
        var result = _Run(driver, edited);

        IncrementalGeneratorAssertions
            .StepReasons(result, "ReferencedFunctions")
            .Should()
            .Contain(IncrementalStepRunReason.Modified);
        result
            .Diagnostics.Should()
            .ContainSingle(diagnostic => string.Equals(diagnostic.Id, "HF014", StringComparison.Ordinal));
    }

    private static (GeneratorDriver Driver, CSharpCompilation Compilation) _RunInitial()
    {
        var producer = GeneratorTestHelper.EmitReference(
            "CachingProducer",
            _ProducerSource,
            out var producerDiagnostics
        );
        producerDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var compilation = GeneratorTestHelper.CreateCompilation(
            "Caching.Consumer",
            [(_JobsPath, _JobsSource), (_UnrelatedPath, "public sealed class Unrelated { }")],
            [producer]
        );
        var driver = GeneratorCompilation
            .CreateTrackingDriver(new JobsIncrementalSourceGenerator())
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
