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
        ClassDeclarationSyntax classDeclaration,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol? methodSymbol,
        string className,
        Location attributeLocation,
        HashSet<string> usedFunctionNames,
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
        else
        {
            // Check for duplicate function names
            if (!usedFunctionNames.Add(attributeValues.functionName!))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateFunctionName,
                        attributeLocation,
                        attributeValues.functionName
                    )
                );
            }
        }

        // Validate cron expression
        JobFunctionValidator.ValidateCronExpression(
            attributeValues.cronExpression,
            className,
            attributeLocation,
            context
        );
    }
}
