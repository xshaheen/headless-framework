// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Data.Common;
using Headless.DistributedLocks;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Sequences;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Analyzers;
using Headless.UnitOfWork.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// Runs the analyzer and its code fix over test sources compiled against the real receiver assemblies, so every rule is
/// checked against the metadata names consumers compile against.
/// </summary>
internal static class AnalyzerHarness
{
    private static readonly ImmutableArray<MetadataReference> _References =
        GeneratorCompilation.LoadedAssemblyReferences(
            typeof(IUnitOfWork).Assembly,
            typeof(DbContext).Assembly,
            typeof(DbConnection).Assembly,
            typeof(HeadlessDbConnectionUnitOfWorkExtensions).Assembly,
            typeof(HeadlessDbContextUnitOfWorkExtensions).Assembly,
            typeof(IBus).Assembly,
            typeof(IQueue).Assembly,
            typeof(UnitOfWorkOutbox).Assembly,
            typeof(IJobScheduler).Assembly,
            typeof(IDistributedLock).Assembly,
            typeof(DistributedLockExtensions).Assembly,
            typeof(ISequenceGenerator).Assembly,
            typeof(IFencedLeases).Assembly,
            typeof(IIdempotentOperations).Assembly
        );

    /// <summary>Runs the analyzer over <paramref name="source"/>, which must compile without errors.</summary>
    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        CancellationToken cancellationToken,
        NullableContextOptions nullable = NullableContextOptions.Enable
    )
    {
        var document = _CreateDocument(source, nullable);
        return await _AnalyzeAsync(document, cancellationToken);
    }

    /// <summary>
    /// Applies the code fix registered for the diagnostic at <paramref name="diagnosticIndex"/> and returns the fixed
    /// source after asserting it compiles with no warnings and no remaining analyzer diagnostic.
    /// </summary>
    public static async Task<string> FixAsync(
        string source,
        CancellationToken cancellationToken,
        int diagnosticIndex = 0
    )
    {
        var document = _CreateDocument(source, NullableContextOptions.Enable);
        var diagnostics = await _AnalyzeAsync(document, cancellationToken);
        var actions = await _RegisterFixesAsync(document, diagnostics[diagnosticIndex], cancellationToken);

        actions.Should().ContainSingle("a one-to-one call offers exactly one fix");

        var fixedDocument = await _ApplyAsync(actions[0], document, cancellationToken);
        return await _AssertCleanAsync(fixedDocument, cancellationToken);
    }

    /// <summary>Applies Fix All to every diagnostic in the document and returns the fixed source.</summary>
    public static async Task<string> FixAllAsync(string source, CancellationToken cancellationToken)
    {
        var document = _CreateDocument(source, NullableContextOptions.Enable);
        var diagnostics = await _AnalyzeAsync(document, cancellationToken);
        var provider = new EnlistedReceiverCodeFixProvider();
        var actions = await _RegisterFixesAsync(document, diagnostics[0], cancellationToken);
        var fixAllContext = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            actions[0].EquivalenceKey,
            provider.FixableDiagnosticIds,
            new FixedDiagnosticProvider(diagnostics),
            cancellationToken
        );

        var fixAll = await provider.GetFixAllProvider().GetFixAsync(fixAllContext);
        fixAll.Should().NotBeNull();

        var fixedDocument = await _ApplyAsync(fixAll!, document, cancellationToken);
        return await _AssertCleanAsync(fixedDocument, cancellationToken);
    }

    /// <summary>Returns the fixes the code fix registers for the first diagnostic in <paramref name="source"/>.</summary>
    public static async Task<ImmutableArray<CodeAction>> GetFixesAsync(
        string source,
        CancellationToken cancellationToken
    )
    {
        var document = _CreateDocument(source, NullableContextOptions.Enable);
        var diagnostics = await _AnalyzeAsync(document, cancellationToken);

        diagnostics.Should().NotBeEmpty();

        return await _RegisterFixesAsync(document, diagnostics[0], cancellationToken);
    }

    private static Document _CreateDocument(string source, NullableContextOptions nullable)
    {
#pragma warning disable CA2000 // False positive: the returned document uses this workspace's services for the rest of the test, so it must outlive this method.
        var workspace = new AdhocWorkspace();
#pragma warning restore CA2000
        var project = workspace.AddProject(
            ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Default,
                "Tests.Sources",
                "Tests.Sources",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: nullable
                ),
                parseOptions: GeneratorCompilation.ParseOptions,
                metadataReferences: _References
            )
        );

        return workspace.AddDocument(project.Id, "Source.cs", SourceText.From(source));
    }

    private static async Task<ImmutableArray<Diagnostic>> _AnalyzeAsync(
        Document document,
        CancellationToken cancellationToken
    )
    {
        var compilation = await document.Project.GetCompilationAsync(cancellationToken);
        compilation.Should().NotBeNull();

        compilation!
            .GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty("test sources must compile before the analyzer runs");

        var diagnostics = await compilation
            .WithAnalyzers([new AutonomousReceiverAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);

        return [.. diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    private static async Task<ImmutableArray<CodeAction>> _RegisterFixesAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken
    )
    {
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);

        await new EnlistedReceiverCodeFixProvider().RegisterCodeFixesAsync(context);
        return actions.ToImmutable();
    }

    private static async Task<Document> _ApplyAsync(
        CodeAction action,
        Document document,
        CancellationToken cancellationToken
    )
    {
        var operations = await action.GetOperationsAsync(cancellationToken);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        return apply.ChangedSolution.GetDocument(document.Id)!;
    }

    private static async Task<string> _AssertCleanAsync(Document document, CancellationToken cancellationToken)
    {
        var compilation = await document.Project.GetCompilationAsync(cancellationToken);

        compilation!
            .GetDiagnostics(cancellationToken)
            // Moving a call off an injected service can leave that service unread (CS9113); any other warning, such as
            // a possible null dereference of the unit, means the rewrite introduced a defect.
            .Where(diagnostic =>
                diagnostic.Severity >= DiagnosticSeverity.Warning
                && !string.Equals(diagnostic.Id, "CS9113", StringComparison.Ordinal)
            )
            .Should()
            .BeEmpty("the fixed source must compile without new errors or warnings");

        var remaining = await compilation
            .WithAnalyzers([new AutonomousReceiverAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);

        remaining.Should().BeEmpty("a rewritten call goes through the enlisted receiver");

        return (await document.GetTextAsync(cancellationToken)).ToString();
    }

    private sealed class FixedDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken
        ) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken
        ) => Task.FromResult<IEnumerable<Diagnostic>>([]);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken
        ) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
