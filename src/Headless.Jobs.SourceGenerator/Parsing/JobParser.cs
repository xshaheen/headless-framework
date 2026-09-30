// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
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
        JobValidator.ValidateAttribute(compilation, values, classSymbol.Name, attributeLocation, diagnostics);

        var argsTypeName = argsType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var job =
            diagnostics.Count > 0
                ? null
                : new JobModel(
                    classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    _InvokerName(classSymbol),
                    argsTypeName,
                    _GetDisposal(compilation, classSymbol),
                    values.Identity!,
                    values.CronExpression,
                    values.TimeZone,
                    values.Priority,
                    values.MaxConcurrency,
                    values.OnMissedRun,
                    values.MissedRunGraceSeconds,
                    values.OnOverlap,
                    values.ContractVersion,
                    values.Policy?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
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

    private static JobDisposal _GetDisposal(Compilation compilation, INamedTypeSymbol classSymbol)
    {
        if (_Implements(compilation, classSymbol, "System.IAsyncDisposable"))
        {
            return JobDisposal.Async;
        }

        return _Implements(compilation, classSymbol, "System.IDisposable") ? JobDisposal.Sync : JobDisposal.None;
    }

    private static bool _Implements(Compilation compilation, INamedTypeSymbol classSymbol, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        return type is not null && classSymbol.AllInterfaces.Contains(type, SymbolEqualityComparer.Default);
    }

    /// <summary>
    /// Names the invoker after the class's full name. Jobs are top-level classes, so the namespace-qualified name is
    /// unique within the assembly.
    /// </summary>
    private static string _InvokerName(INamedTypeSymbol classSymbol)
    {
        var name = classSymbol.ToDisplayString();
        var builder = new StringBuilder("Invoke_", name.Length + 7);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }
}

/// <summary>The values of one <c>[Job]</c> attribute application, read without interpreting them.</summary>
internal sealed record JobAttributeValues(
    string? Identity,
    string? CronExpression,
    string? TimeZone,
    int Priority,
    int MaxConcurrency,
    int? OnMissedRun,
    int? MissedRunGraceSeconds,
    int? OnOverlap,
    string ContractVersion,
    INamedTypeSymbol? Policy
)
{
    public static JobAttributeValues Read(AttributeData attribute)
    {
        var identity =
            attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
        string? cronExpression = null;
        string? timeZone = null;
        var priority = 0; // JobPriority.Normal
        var maxConcurrency = 0;
        var contractVersion = "1";
        INamedTypeSymbol? policy = null;

        // The recovery knobs are read only when actually written. That distinguishes "unset" (fall through to the
        // scheduler-wide default at creation) from "explicitly set to the framework default", which the property
        // getters cannot express because attribute arguments cannot be nullable value types.
        int? onMissedRun = null;
        int? missedRunGraceSeconds = null;
        int? onOverlap = null;

        foreach (var named in attribute.NamedArguments)
        {
            var value = named.Value.Value;
            switch (named.Key)
            {
                case "Cron":
                    cronExpression = value as string;
                    break;
                case "TimeZone":
                    timeZone = value as string;
                    break;
                case "Priority" when value is int priorityValue:
                    priority = priorityValue;
                    break;
                case "MaxConcurrency" when value is int concurrencyValue:
                    maxConcurrency = concurrencyValue;
                    break;
                case "ContractVersion":
                    contractVersion = value as string ?? string.Empty;
                    break;
                case "Policy":
                    policy = value as INamedTypeSymbol;
                    break;
                case "OnMissedRun" when value is int missedRunValue:
                    onMissedRun = missedRunValue;
                    break;
                case "MissedRunGraceSeconds" when value is int graceValue:
                    missedRunGraceSeconds = graceValue;
                    break;
                case "OnOverlap" when value is int overlapValue:
                    onOverlap = overlapValue;
                    break;
            }
        }

        return new(
            identity,
            cronExpression,
            timeZone,
            priority,
            maxConcurrency,
            onMissedRun,
            missedRunGraceSeconds,
            onOverlap,
            contractVersion,
            policy
        );
    }
}
