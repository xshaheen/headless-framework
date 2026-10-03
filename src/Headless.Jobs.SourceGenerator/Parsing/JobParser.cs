// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator.Models;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.Jobs.SourceGenerator.Validation;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Parsing;

/// <summary>
/// Turns one <c>[Job]</c> class into a <see cref="JobResult"/>. All semantic work for a job happens here, so the
/// result is the only thing a later step sees.
/// </summary>
internal static class JobParser
{
    public static bool IsCandidate(SyntaxNode node, CancellationToken _) =>
        node is ClassDeclarationSyntax or RecordDeclarationSyntax;

    public static JobResult? Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (
            context.TargetNode is not TypeDeclarationSyntax declaration
            || context.TargetSymbol is not INamedTypeSymbol classSymbol
            || context.Attributes.IsEmpty
        )
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        var diagnostics = new List<DiagnosticInfo>();
        var classIdentifier = declaration.Identifier;
        var attributeLocation = attribute.ApplicationSyntaxReference is { } syntaxReference
            ? syntaxReference.SyntaxTree.GetLocation(syntaxReference.Span)
            : classIdentifier.GetLocation();

        JobValidator.ValidateClass(classSymbol, classIdentifier, diagnostics);

        var argsType = _ResolveArgsType(compilation, classSymbol, out var implementsJob);
        if (!implementsJob)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(DiagnosticDescriptors.MissingJobInterface, attributeLocation, classSymbol.Name)
            );
        }

        var values = JobAttributeValues.Read(attribute);
        JobValidator.ValidateAttribute(values, classSymbol.Name, attributeLocation, diagnostics);
        if (values.FailurePolicy is { } failurePolicy)
        {
            JobValidator.ValidateFailurePolicy(
                compilation,
                failurePolicy,
                classSymbol.Name,
                attributeLocation,
                diagnostics
            );
        }

        var argsTypeName = argsType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var job =
            diagnostics.Count > 0
                ? null
                : new JobModel(
                    classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    // Jobs are top-level classes, so the namespace-qualified name keeps the invoker unique within the
                    // assembly.
                    HandlerSymbols.ToMemberName("Invoke_", classSymbol.ToDisplayString()),
                    argsTypeName,
                    HandlerSymbols.GetDisposal(compilation, classSymbol),
                    values.Identity!,
                    values.CronExpression,
                    values.TimeZone,
                    values.Priority,
                    values.MaxConcurrency,
                    values.OnMissedRun,
                    values.MissedRunGraceSeconds,
                    values.OnOverlap,
                    values.ContractVersion,
                    values.FailurePolicy?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                );

        return new(
            job,
            values.Identity,
            argsTypeName,
            LocationInfo.From(attributeLocation),
            diagnostics.ToEquatableArray()
        );
    }

    /// <summary>
    /// Returns the <c>TArgs</c> of the class's single <c>IJob&lt;TArgs&gt;</c>, or <see langword="null"/> for an
    /// <c>IJob</c> class. A class that implements neither, or more than one, is not a job: one class has one entry
    /// point, and scheduling resolves a job from exactly one argument type or class.
    /// </summary>
    private static ITypeSymbol? _ResolveArgsType(
        Compilation compilation,
        INamedTypeSymbol classSymbol,
        out bool implementsJob
    )
    {
        var plainJob = compilation.GetTypeByMetadataName(SourceGeneratorConstants.JobInterfaceMetadataName);
        var genericJob = compilation.GetTypeByMetadataName(SourceGeneratorConstants.GenericJobInterfaceMetadataName);
        var implementsPlain =
            plainJob is not null && classSymbol.AllInterfaces.Contains(plainJob, SymbolEqualityComparer.Default);
        var genericImplementations = genericJob is null
            ? []
            : classSymbol
                .AllInterfaces.Where(type => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, genericJob))
                .ToList();

        implementsJob = (implementsPlain ? 1 : 0) + genericImplementations.Count == 1;
        return implementsJob && genericImplementations.Count == 1 ? genericImplementations[0].TypeArguments[0] : null;
    }
}
