// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;

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
        (
            string? functionName,
            string? cronExpression,
            int taskPriority,
            int maxConcurrency,
            int? onMissedRun,
            int? missedRunGraceSeconds,
            int? onOverlap
        ) attributeValues,
        string methodName,
        string className,
        Location attributeLocation,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        if (string.IsNullOrWhiteSpace(attributeValues.functionName))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MissingFunctionName,
                    attributeLocation,
                    methodName,
                    className
                )
            );
        }

        JobFunctionValidator.ValidateCronExpression(
            attributeValues.cronExpression,
            className,
            attributeLocation,
            diagnostics
        );

        if (attributeValues.taskPriority is < 0 or > 3)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidJobPriority,
                    attributeLocation,
                    attributeValues.taskPriority
                )
            );
        }

        if (attributeValues.maxConcurrency < 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidMaxConcurrency,
                    attributeLocation,
                    attributeValues.maxConcurrency
                )
            );
        }

        if (attributeValues.onMissedRun is not null and not 0 and not 1)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidMissedRunPolicy,
                    attributeLocation,
                    attributeValues.onMissedRun
                )
            );
        }

        if (attributeValues.missedRunGraceSeconds is <= 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidMissedRunGrace,
                    attributeLocation,
                    attributeValues.missedRunGraceSeconds
                )
            );
        }

        if (attributeValues.onOverlap is not null and not 0 and not 1)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidOverlapPolicy,
                    attributeLocation,
                    attributeValues.onOverlap
                )
            );
        }
    }
}
