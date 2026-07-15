// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Headless.Jobs.SourceGenerator.AttributeSyntaxes;
using Headless.Jobs.SourceGenerator.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Validation;

internal static class CompilationCollisionValidator
{
    public static bool Validate(
        ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
        Compilation compilation,
        SourceProductionContext context
    )
    {
        var functionNames = new HashSet<string>(StringComparer.Ordinal);
        var requestTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var isValid = true;

        foreach (var (_, methodDeclaration) in methodPairs)
        {
            var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
            var attribute = _GetJobFunctionAttribute(methodSymbol);
            if (attribute == null)
            {
                continue;
            }

            var (functionName, _, _, _) = attribute.GetJobFunctionAttributeValues();
            var location = _GetAttributeLocation(attribute, methodDeclaration);
            if (!string.IsNullOrWhiteSpace(functionName) && !functionNames.Add(functionName!))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.DuplicateFunctionName, location, functionName)
                );
                isValid = false;
            }

            var requestType = GetRequestType(methodSymbol);
            if (requestType != null && !requestTypes.Add(requestType))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateRequestType,
                        location,
                        requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    )
                );
                isValid = false;
            }
        }

        return isValid;
    }

    public static ITypeSymbol? GetRequestType(IMethodSymbol? methodSymbol)
    {
        return methodSymbol
            ?.Parameters.Select(parameter => parameter.Type)
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

    private static AttributeData? _GetJobFunctionAttribute(IMethodSymbol? methodSymbol)
    {
        return methodSymbol
            ?.GetAttributes()
            .FirstOrDefault(attribute =>
                string.Equals(
                    attribute.AttributeClass?.Name,
                    SourceGeneratorConstants.JobFunctionAttributeName,
                    StringComparison.Ordinal
                )
            );
    }

    private static Location _GetAttributeLocation(AttributeData attribute, MethodDeclarationSyntax methodDeclaration)
    {
#pragma warning disable MA0045 // Incremental generator execution is synchronous.
        return attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? methodDeclaration.Identifier.GetLocation();
#pragma warning restore MA0045
    }
}
