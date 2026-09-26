// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator.Utilities;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>
/// Handles validation of JobFunction attributes and their usage.
/// </summary>
internal static class JobFunctionValidator
{
    /// <summary>
    /// Validates class and method accessibility, and that the declaring class is not abstract.
    /// </summary>
    public static void ValidateClassAndMethod(
        ClassDeclarationSyntax classDeclaration,
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol? classSymbol,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        if (!_IsPublicOrInternal(classDeclaration.Modifiers))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.ClassAccessibility,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                )
            );
        }

        if (!_IsPublicOrInternal(methodDeclaration.Modifiers))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MethodAccessibility,
                    methodDeclaration.Identifier.GetLocation(),
                    methodDeclaration.Identifier.Text
                )
            );
        }

        if (classSymbol?.IsAbstract == true)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.AbstractClass,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                )
            );
        }
    }

    /// <summary>
    /// Validates cron expression format; configuration placeholders (<c>%Key%</c>) are resolved at runtime and skipped.
    /// </summary>
    public static void ValidateCronExpression(
        string? cronExpression,
        string className,
        Location attributeLocation,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        if (string.IsNullOrEmpty(cronExpression) || _IsConfigurationExpression(cronExpression!))
        {
            return;
        }

        if (!CronValidator.IsValidCronExpression(cronExpression!))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidCronExpression,
                    attributeLocation,
                    cronExpression,
                    className
                )
            );
        }
    }

    /// <summary>
    /// Validates that a class is not nested.
    /// </summary>
    public static void ValidateNotNestedClass(
        ClassDeclarationSyntax classDeclaration,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        if (classDeclaration.Parent is ClassDeclarationSyntax)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.NestedClass,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                )
            );
        }
    }

    /// <summary>
    /// Validates that JobFunction method parameters are only allowed types.
    /// </summary>
    public static void ValidateMethodParameters(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        foreach (var parameter in methodSymbol.Parameters)
        {
            var parameterTypeString = parameter.Type.ToDisplayString();
            if (_IsAllowedParameterType(parameter.Type, parameterTypeString))
            {
                continue;
            }

            var parameterSyntax = methodDeclaration.ParameterList.Parameters.FirstOrDefault(p =>
                string.Equals(p.Identifier.Text, parameter.Name, StringComparison.Ordinal)
            );

            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidMethodParameter,
                    parameterSyntax?.GetLocation() ?? methodDeclaration.Identifier.GetLocation(),
                    methodDeclaration.Identifier.Text,
                    parameter.Name,
                    parameterTypeString
                )
            );
        }
    }

    private static bool _IsAllowedParameterType(ITypeSymbol parameterType, string parameterTypeString)
    {
        if (
            string.Equals(
                parameterTypeString,
                SourceGeneratorConstants.CancellationTokenTypeName,
                StringComparison.Ordinal
            )
            || string.Equals(
                parameterTypeString,
                SourceGeneratorConstants.BaseJobFunctionContextTypeName,
                StringComparison.Ordinal
            )
        )
        {
            return true;
        }

        if (
            parameterType is INamedTypeSymbol { IsGenericType: true } genericType
            && string.Equals(
                genericType.ConstructedFrom.ToDisplayString(),
                "Headless.Jobs.Base.JobFunctionContext<T>",
                StringComparison.Ordinal
            )
        )
        {
            return true;
        }

        return parameterType is INamedTypeSymbol namedType
            && string.Equals(namedType.Name, "JobFunctionContext", StringComparison.Ordinal)
            && namedType.ContainingNamespace?.ToDisplayString() is "Headless.Jobs" or "Headless.Jobs.Base";
    }

    private static bool _IsPublicOrInternal(SyntaxTokenList modifiers) =>
        modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword));

    private static bool _IsConfigurationExpression(string cronExpression) =>
        cronExpression.StartsWith(SourceGeneratorConstants.ConfigExpressionPrefix, StringComparison.Ordinal)
        && cronExpression.EndsWith(SourceGeneratorConstants.ConfigExpressionSuffix, StringComparison.Ordinal)
        && cronExpression.Length >= SourceGeneratorConstants.MinConfigExpressionLength;
}
