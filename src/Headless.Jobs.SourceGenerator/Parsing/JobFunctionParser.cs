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
            || context.TargetSymbol is not IMethodSymbol methodSymbol
            || context.Attributes.IsEmpty
        )
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var semanticModel = context.SemanticModel;
        var attribute = context.Attributes[0];
        var diagnostics = new List<DiagnosticInfo>();

        JobFunctionValidator.ValidateClassAndMethod(
            classDeclaration,
            method,
            semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken),
            diagnostics
        );
        ConstructorValidator.ValidateMultipleConstructors(classDeclaration, semanticModel, diagnostics);
        JobFunctionValidator.ValidateNotNestedClass(classDeclaration, diagnostics);

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
            _ParseClass(classDeclaration, semanticModel, cancellationToken),
            method.Identifier.Text,
            method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
            SourceGeneratorUtilities.IsMethodAwaitable(method),
            _GetInvocationArguments(method),
            _GetGenericTypeName(method, semanticModel, cancellationToken),
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

    private static string _GetGenericTypeName(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var genericTypeName = string.Empty;
        var genericPrefix = SourceGeneratorConstants.BaseGenericJobFunctionContextTypeName.Replace("`1", "<");
        foreach (var parameter in method.ParameterList.Parameters)
        {
            if (parameter.Type == null)
            {
                continue;
            }

            var typeName =
                semanticModel.GetSymbolInfo(parameter.Type, cancellationToken).Symbol?.ToDisplayString()
                ?? parameter.Type.ToString();
            if (!typeName.StartsWith(genericPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var startIndex = typeName.IndexOf('<') + 1;
            var endIndex = typeName.LastIndexOf('>');
            if (startIndex > 0 && endIndex > startIndex)
            {
                genericTypeName = typeName.Substring(startIndex, endIndex - startIndex);
            }
        }

        return genericTypeName;
    }

    private static JobClassModel _ParseClass(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var isStatic = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
        return new(
            SourceGeneratorUtilities.GetFullClassName(classDeclaration),
            SourceGeneratorUtilities.GetNamespace(classDeclaration),
            classDeclaration.Identifier.Text,
            isStatic,
            isStatic
                ? EquatableArray<ConstructorParameterModel>.Empty
                : _ParseConstructor(classDeclaration, semanticModel, cancellationToken)
        );
    }

    /// <summary>
    /// Picks the constructor the factory calls: the primary constructor, else the one marked
    /// <c>[JobsConstructor]</c>, else the first public one, else the parameterless default.
    /// </summary>
    private static EquatableArray<ConstructorParameterModel> _ParseConstructor(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var constructors = classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var markedConstructor = constructors.Find(constructor =>
            semanticModel.GetDeclaredSymbol(constructor, cancellationToken) is { } symbol
            && symbol.GetAttributes().Any(SourceGeneratorUtilities.IsJobsConstructorAttribute)
        );
        var selectedConstructor =
            markedConstructor
            ?? constructors.Find(constructor =>
                constructor.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
            );

        var isPrimaryConstructor = classDeclaration.ParameterList?.Parameters.Count > 0;
        var parameters = isPrimaryConstructor
            ? classDeclaration.ParameterList!.Parameters
            : selectedConstructor?.ParameterList.Parameters ?? default;

        var result = new List<ConstructorParameterModel>();
        foreach (var parameter in parameters)
        {
            var parameterName = SourceGeneratorUtilities.FirstLetterToLower(parameter.Identifier.Text);
            if (string.Equals(parameterName, "serviceProvider", StringComparison.Ordinal) || parameter.Type == null)
            {
                result.Add(new(parameterName, null, null));
                continue;
            }

            var parameterSymbol = isPrimaryConstructor
                ? _GetPrimaryConstructorParameterSymbol(classDeclaration, parameter, semanticModel, cancellationToken)
                : semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
            var typeName =
                semanticModel.GetSymbolInfo(parameter.Type, cancellationToken).Symbol?.ToDisplayString()
                ?? parameter.Type.ToString();
            var keyedServiceAttribute = parameterSymbol
                ?.GetAttributes()
                .FirstOrDefault(SourceGeneratorUtilities.IsFromKeyedServicesAttribute);
            var serviceKey = keyedServiceAttribute is null
                ? null
                : SourceGeneratorUtilities.GetServiceKey(keyedServiceAttribute);

            result.Add(new(parameterName, typeName, serviceKey));
        }

        return result.ToEquatableArray();
    }

    private static IParameterSymbol? _GetPrimaryConstructorParameterSymbol(
        ClassDeclarationSyntax classDeclaration,
        ParameterSyntax parameter,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var primaryConstructor = semanticModel
            .GetDeclaredSymbol(classDeclaration, cancellationToken)
            ?.Constructors.FirstOrDefault(constructor => constructor.Parameters.Length > 0);

        return primaryConstructor?.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, parameter.Identifier.Text, StringComparison.Ordinal)
        );
    }
}
