// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace Headless.UnitOfWork.Analyzers.CodeFixes;

/// <summary>
/// Rewrites a flagged call onto the enlisted receiver of the unit of work in scope, for example
/// <c>bus.PublishAsync(message, ct)</c> to <c>unit.Outbox.PublishAsync(message, ct)</c>.
/// </summary>
/// <remarks>
/// The analyzer decides whether a call is one-to-one and records the unit and receiver in the diagnostic's properties;
/// this assembly only rewrites. It carries the rule IDs and property names as literals because it does not reference
/// the analyzer assembly: the analyzer packs this assembly, so a reference back would be a cycle.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EnlistedReceiverCodeFixProvider)), Shared]
public sealed class EnlistedReceiverCodeFixProvider : CodeFixProvider
{
    private const string _UnitProperty = "Unit";
    private const string _ReceiverProperty = "Receiver";
    private const string _UnitOfWorkNamespace = "Headless.UnitOfWork";
    private const string _EquivalenceKey = "Headless.UnitOfWork.Analyzers.EnlistedReceiver";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["HF2001"];

    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(
            static async (context, document, diagnostics) =>
                await _RewriteAsync(document, diagnostics, context.CancellationToken).ConfigureAwait(false)
        );

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!_TryReadTarget(diagnostic, out var unit, out var receiver))
            {
                continue;
            }

            var target = unit + "." + receiver;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Call through '" + target + "'",
                    cancellationToken => _RewriteAsync(context.Document, [diagnostic], cancellationToken),
                    _EquivalenceKey
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    /// <summary>Rewrites every flagged call in one pass, then adds the namespace import once if any rewrite needs it.</summary>
    private static async Task<Document> _RewriteAsync(
        Document document,
        ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
        {
            return document;
        }

        var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
        var needsImport = false;

        foreach (var diagnostic in diagnostics)
        {
            if (
                !_TryReadTarget(diagnostic, out var unit, out var receiver)
                || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess } call
            )
            {
                continue;
            }

            var enlisted = SyntaxFactory.ParseExpression(unit + "." + receiver);
            replacements[memberAccess.Expression] = enlisted.WithTriviaFrom(memberAccess.Expression);

            // The accessors are extension members in Headless.UnitOfWork; they bind only where that namespace is imported.
            needsImport |=
                model
                    .GetSpeculativeSymbolInfo(call.SpanStart, enlisted, SpeculativeBindingOption.BindAsExpression)
                    .Symbol
                    is null;
        }

        if (replacements.Count == 0)
        {
            return document;
        }

        var rewritten = root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);

        if (needsImport && rewritten is CompilationUnitSyntax compilationUnit)
        {
            var generator = SyntaxGenerator.GetGenerator(document);
            rewritten = generator
                .AddNamespaceImports(compilationUnit, generator.NamespaceImportDeclaration(_UnitOfWorkNamespace))
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        return document.WithSyntaxRoot(rewritten);
    }

    private static bool _TryReadTarget(Diagnostic diagnostic, out string unit, out string receiver)
    {
        unit = string.Empty;
        receiver = string.Empty;

        if (
            !diagnostic.Properties.TryGetValue(_UnitProperty, out var unitValue)
            || !diagnostic.Properties.TryGetValue(_ReceiverProperty, out var receiverValue)
            || string.IsNullOrEmpty(unitValue)
            || string.IsNullOrEmpty(receiverValue)
        )
        {
            return false;
        }

        unit = unitValue!;
        receiver = receiverValue!;
        return true;
    }
}
