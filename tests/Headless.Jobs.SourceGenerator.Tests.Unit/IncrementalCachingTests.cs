// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Reflection;
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
        "JobFunctions",
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
        using Headless.Jobs.Base;

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

        public sealed class InvoiceJobs
        {
            [JobFunction("invoice.send", "0 */5 * * * *")]
            [JobExecuteMiddleware<AuditMiddleware>]
            public Task SendAsync(JobFunctionContext<Invoice> context, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            [JobFunction("invoice.cleanup")]
            public void Cleanup() { }
        }
        """;

    private const string _ProducerSource = """
        using Headless.Jobs.Base;

        namespace Upstream;

        public sealed class ProducerJobs
        {
            [JobFunction("producer.run")]
            public void Run() { }
        }
        """;

    [Fact]
    public void should_reuse_every_tracked_step_when_an_unrelated_file_changes()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(compilation, _UnrelatedPath, "public sealed class Unrelated { public int Value; }");
        var result = _Run(driver, edited);

        _AssertAllStepsReused(result);
        _AssertNoSymbolsOrSyntax(result);
    }

    [Fact]
    public void should_reuse_every_tracked_step_when_an_unrelated_file_is_added()
    {
        var (driver, compilation) = _RunInitial();

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                "public sealed class Added { }",
                GeneratorTestHelper.ParseOptions,
                "added.cs",
                cancellationToken: AbortToken
            )
        );
        var result = _Run(driver, edited);

        _AssertAllStepsReused(result);
    }

    [Fact]
    public void should_not_re_emit_source_when_an_edit_only_moves_job_functions()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(compilation, _JobsPath, "// moved\n\n\n" + _JobsSource);
        var result = _Run(driver, edited);

        // Locations live beside the emission model, so moving code refreshes diagnostics but not generated source.
        _Reasons(result, "RegistrationModel")
            .Should()
            .OnlyContain(reason =>
                reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged
            );
        _AssertNoSymbolsOrSyntax(result);
    }

    [Fact]
    public void should_regenerate_when_a_job_function_changes()
    {
        var (driver, compilation) = _RunInitial();

        var edited = _ReplaceText(
            compilation,
            _JobsPath,
            _JobsSource.Replace("0 */5 * * * *", "0 */10 * * * *", StringComparison.Ordinal)
        );
        var result = _Run(driver, edited);

        _Reasons(result, "JobFunctions").Should().Contain(IncrementalStepRunReason.Modified);
        _Reasons(result, "RegistrationModel").Should().Contain(IncrementalStepRunReason.Modified);
        result.GeneratedSources.Single().SourceText.ToString().Should().Contain("0 */10 * * * *");
    }

    [Fact]
    public void should_revalidate_middleware_targets_when_a_referenced_function_is_renamed()
    {
        var (driver, compilation) = _RunInitial();
        var renamedProducer = GeneratorTestHelper.EmitReference(
            "CachingProducer",
            _ProducerSource.Replace("producer.run", "producer.renamed", StringComparison.Ordinal),
            out _
        );

        var edited = compilation.RemoveReferences(compilation.References.Last()).AddReferences(renamedProducer);
        var result = _Run(driver, edited);

        _Reasons(result, "ReferencedFunctions").Should().Contain(IncrementalStepRunReason.Modified);
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
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new JobsIncrementalSourceGenerator().AsSourceGenerator()],
            parseOptions: GeneratorTestHelper.ParseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );
        driver = driver.RunGenerators(compilation);

        var initial = driver.GetRunResult().Results.Single();
        initial.Exception.Should().BeNull();
        initial.Diagnostics.Should().BeEmpty();
        initial.GeneratedSources.Should().ContainSingle();
        _AssertNoSymbolsOrSyntax(initial);
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

    private static void _AssertAllStepsReused(GeneratorRunResult result)
    {
        foreach (var step in _TrackedSteps)
        {
            _Reasons(result, step)
                .Should()
                .NotBeEmpty($"step '{step}' must run")
                .And.OnlyContain(
                    reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged,
                    $"step '{step}' must be reused after an unrelated edit"
                );
        }
    }

    private static IncrementalStepRunReason[] _Reasons(GeneratorRunResult result, string step)
    {
        result.TrackedSteps.Should().ContainKey(step);
        return [.. result.TrackedSteps[step].SelectMany(run => run.Outputs).Select(output => output.Reason)];
    }

    private static void _AssertNoSymbolsOrSyntax(GeneratorRunResult result)
    {
        foreach (var step in _TrackedSteps)
        {
            foreach (var (value, _) in result.TrackedSteps[step].SelectMany(run => run.Outputs))
            {
                _Visit(value, $"{step}", new HashSet<object>(ReferenceEqualityComparer.Instance));
            }
        }
    }

    private static void _Visit(object? value, string path, HashSet<object> visited)
    {
        if (value is null || value is string || value is DiagnosticDescriptor || value.GetType().IsPrimitive)
        {
            return;
        }

        value
            .Should()
            .NotBeAssignableTo<ISymbol>(path)
            .And.NotBeAssignableTo<SyntaxNode>(path)
            .And.NotBeAssignableTo<SyntaxTree>(path)
            .And.NotBeAssignableTo<SemanticModel>(path)
            .And.NotBeAssignableTo<Compilation>(path)
            .And.NotBeAssignableTo<Location>(path);

        var type = value.GetType();
        if (type.IsEnum || (!type.IsValueType && !visited.Add(value)))
        {
            return;
        }

        if (value is IEnumerable items)
        {
            var index = 0;
            foreach (var item in items)
            {
                _Visit(item, $"{path}[{index++}]", visited);
            }

            return;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            _Visit(field.GetValue(value), $"{path}.{field.Name}", visited);
        }
    }
}
