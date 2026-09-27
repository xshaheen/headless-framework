// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator.AttributeSyntaxes;
using Headless.Jobs.SourceGenerator.Models;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.Jobs.SourceGenerator.Validation;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Parsing;

/// <summary>
/// Turns one <c>[JobFunction]</c> method into a <see cref="JobFunctionResult"/>. All semantic work for a function
/// happens here, so the result is the only thing a later step sees.
/// </summary>
internal static class JobFunctionParser
{
    public static bool IsCandidate(SyntaxNode node, CancellationToken _) =>
        node is MethodDeclarationSyntax { Parent: ClassDeclarationSyntax };

    public static JobFunctionResult? Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (
            context.TargetNode is not MethodDeclarationSyntax { Parent: ClassDeclarationSyntax classDeclaration } method
            || context.TargetSymbol is not IMethodSymbol { ContainingType: { } classSymbol } methodSymbol
            || context.Attributes.IsEmpty
        )
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var attribute = context.Attributes[0];
        var diagnostics = new List<DiagnosticInfo>();
        var classIdentifier = classDeclaration.Identifier;
        var constructors = _SelectConstructors(classSymbol);

        JobFunctionValidator.ValidateClassAndMethod(classSymbol, classIdentifier, method, diagnostics);
        ConstructorValidator.ValidateMultipleConstructors(constructors, classIdentifier, diagnostics);
        JobFunctionValidator.ValidateNotNestedClass(classSymbol, classIdentifier, diagnostics);

        var values = attribute.GetJobFunctionAttributeValues();
        var attributeLocation = attribute.ApplicationSyntaxReference is { } syntaxReference
            ? syntaxReference.SyntaxTree.GetLocation(syntaxReference.Span)
            : method.Identifier.GetLocation();
        AttributeValidator.ValidateJobFunctionAttribute(
            values,
            method.Identifier.Text,
            classDeclaration.Identifier.Text,
            attributeLocation,
            diagnostics
        );
        JobFunctionValidator.ValidateMethodParameters(method, methodSymbol, diagnostics);

        var function = new JobFunctionModel(
            _ParseClass(classSymbol, constructors),
            method.Identifier.Text,
            method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
            SourceGeneratorUtilities.IsMethodAwaitable(method),
            _GetInvocationArguments(method),
            GetRequestType(methodSymbol)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            values.functionName,
            values.cronExpression,
            values.taskPriority,
            values.maxConcurrency,
            values.onMissedRun,
            values.missedRunGraceSeconds,
            values.onOverlap,
            _GetContractVersion(attribute)
        );

        return new(function, LocationInfo.From(attributeLocation), diagnostics.ToEquatableArray());
    }

    /// <summary>Returns the <c>T</c> of a <c>JobFunctionContext&lt;T&gt;</c> parameter, if any.</summary>
    public static ITypeSymbol? GetRequestType(IMethodSymbol methodSymbol)
    {
        return methodSymbol
            .Parameters.Select(parameter => parameter.Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(type =>
                type.IsGenericType
                && string.Equals(type.OriginalDefinition.MetadataName, "JobFunctionContext`1", StringComparison.Ordinal)
                && string.Equals(
                    type.OriginalDefinition.ContainingNamespace.ToDisplayString(),
                    "Headless.Jobs.Base",
                    StringComparison.Ordinal
                )
            )
            ?.TypeArguments[0];
    }

    private static string _GetContractVersion(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.Key, "ContractVersion", StringComparison.Ordinal))
            {
                return argument.Value.Value as string ?? string.Empty;
            }
        }

        return "1";
    }

    /// <summary>
    /// The arguments the generated delegate passes, in declaration order: the <c>CancellationToken</c>, the context,
    /// or the typed context built from the stored request.
    /// </summary>
    private static EquatableArray<string> _GetInvocationArguments(MethodDeclarationSyntax method)
    {
        var arguments = new List<string>();
        foreach (var parameter in method.ParameterList.Parameters)
        {
            if (parameter.Type == null)
            {
                continue;
            }

            var parameterType = parameter.Type.ToString();
            if (parameterType.Contains("CancellationToken"))
            {
                arguments.Add("cancellationToken");
            }
            else if (parameterType.Contains("JobFunctionContext"))
            {
                arguments.Add(parameterType.Contains("<") ? "genericContext" : "context");
            }
        }

        return arguments.ToEquatableArray();
    }

    private static JobClassModel _ParseClass(INamedTypeSymbol classSymbol, ConstructorSelection constructors)
    {
        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : classSymbol.ContainingNamespace.ToDisplayString();

        return new(
            classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            $"Create{namespaceName.Replace(".", "")}{classSymbol.Name}",
            classSymbol.IsStatic,
            classSymbol.IsStatic
                ? EquatableArray<ConstructorParameterModel>.Empty
                : _ParseConstructorParameters(constructors.Selected)
        );
    }

    /// <summary>
    /// Reads constructors from the type symbol, so every part of a partial class contributes. A constructor is the
    /// primary constructor when it is declared by the type declaration itself rather than by a constructor declaration.
    /// </summary>
    private static ConstructorSelection _SelectConstructors(INamedTypeSymbol classSymbol)
    {
        var declarations = classSymbol.DeclaringSyntaxReferences;
        IMethodSymbol? primary = null;
        var explicitConstructors = new List<IMethodSymbol>();
        foreach (var constructor in classSymbol.InstanceConstructors)
        {
            if (constructor.IsImplicitlyDeclared)
            {
                continue;
            }

            var isPrimary = constructor.DeclaringSyntaxReferences.Any(reference =>
                declarations.Any(declaration =>
                    declaration.SyntaxTree == reference.SyntaxTree && declaration.Span == reference.Span
                )
            );
            if (!isPrimary)
            {
                explicitConstructors.Add(constructor);
            }
            else if (constructor.Parameters.Length > 0)
            {
                primary = constructor;
            }
        }

        var marked = explicitConstructors
            .Where(constructor => constructor.GetAttributes().Any(SourceGeneratorUtilities.IsJobsConstructorAttribute))
            .ToList();
        var selected =
            primary
            ?? marked.FirstOrDefault()
            ?? explicitConstructors.Find(constructor => constructor.DeclaredAccessibility == Accessibility.Public);

        return new(selected, explicitConstructors.Count + (primary is null ? 0 : 1), marked.Count);
    }

    private static EquatableArray<ConstructorParameterModel> _ParseConstructorParameters(IMethodSymbol? constructor)
    {
        if (constructor is null)
        {
            return EquatableArray<ConstructorParameterModel>.Empty;
        }

        var result = new List<ConstructorParameterModel>();
        foreach (var parameter in constructor.Parameters)
        {
            var parameterName = SourceGeneratorUtilities.FirstLetterToLower(parameter.Name);
            if (string.Equals(parameterName, "serviceProvider", StringComparison.Ordinal))
            {
                result.Add(new(parameterName, null, null));
                continue;
            }

            var keyedServiceAttribute = parameter
                .GetAttributes()
                .FirstOrDefault(SourceGeneratorUtilities.IsFromKeyedServicesAttribute);
            var serviceKey = keyedServiceAttribute is null
                ? null
                : SourceGeneratorUtilities.GetServiceKey(keyedServiceAttribute);

            result.Add(
                new(parameterName, parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), serviceKey)
            );
        }

        return result.ToEquatableArray();
    }
}

/// <summary>
/// The constructor the generated factory calls, plus the counts the constructor diagnostics need. The factory uses the
/// primary constructor, else the one marked <c>[JobsConstructor]</c>, else the first public one, else the implicit
/// parameterless constructor.
/// </summary>
internal readonly record struct ConstructorSelection(IMethodSymbol? Selected, int DeclaredCount, int MarkedCount);
