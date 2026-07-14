// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>
/// Handles validation of JobFunction attribute values and usage.
/// </summary>
internal static class AttributeValidator
{
    /// <summary>
    /// Validates all aspects of a JobFunction attribute and its usage.
    /// </summary>
    public static void ValidateJobFunctionAttribute(
        (string? functionName, string? cronExpression, int taskPriority, int maxConcurrency) attributeValues,
        MethodDeclarationSyntax methodDeclaration,
        string className,
        Location attributeLocation,
        SourceProductionContext context
    )
    {
        // Validate function name
        if (string.IsNullOrWhiteSpace(attributeValues.functionName))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.MissingFunctionName,
                    attributeLocation,
                    methodDeclaration.Identifier.Text,
                    className
                )
            );
        }
        // Validate cron expression
        JobFunctionValidator.ValidateCronExpression(
            attributeValues.cronExpression,
            className,
            attributeLocation,
            context
        );

        if (attributeValues.taskPriority is < 0 or > 3)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidJobPriority,
                    attributeLocation,
                    attributeValues.taskPriority
                )
            );
        }

        if (attributeValues.maxConcurrency < 0)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidMaxConcurrency,
                    attributeLocation,
                    attributeValues.maxConcurrency
                )
            );
        }
    }
}
