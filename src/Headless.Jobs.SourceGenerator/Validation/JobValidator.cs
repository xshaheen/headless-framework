// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator.Parsing;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>The per-class <c>[Job]</c> rules. Rules that compare jobs with each other run after collection.</summary>
internal static class JobValidator
{
    /// <summary>Checks that generated code can see and construct the class.</summary>
    /// <remarks>
    /// Accessibility is read from the symbol, not the modifiers of one declaration: a top-level class without a
    /// modifier is internal, and a partial class may declare its accessibility on another part. A <c>file</c>-local
    /// class is rejected because the generated file cannot see it.
    /// </remarks>
    public static void ValidateClass(
        INamedTypeSymbol classSymbol,
        SyntaxToken classIdentifier,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        var location = classIdentifier.GetLocation();
        if (
            classSymbol.IsFileLocal
            || classSymbol.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
        )
        {
            diagnostics.Add(
                DiagnosticInfo.Create(DiagnosticDescriptors.ClassAccessibility, location, classIdentifier.Text)
            );
        }

        if (classSymbol.IsAbstract || classSymbol.IsGenericType)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.AbstractClass, location, classIdentifier.Text));
        }

        if (classSymbol.ContainingType is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.NestedClass, location, classIdentifier.Text));
        }
    }

    /// <summary>Checks the attribute's own values: identity form, cron, and knob ranges.</summary>
    public static void ValidateAttribute(
        JobAttributeValues values,
        string className,
        Location attributeLocation,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        if (!HandlerIdentity.IsValid(values.Identity))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidJobIdentity,
                    attributeLocation,
                    values.Identity ?? "null",
                    className
                )
            );
        }

        if (
            !string.IsNullOrEmpty(values.CronExpression)
            && !values.CronExpression!.StartsWith(
                SourceGeneratorConstants.ConfigExpressionPrefix,
                StringComparison.Ordinal
            )
            && !CronValidator.IsValidCronExpression(values.CronExpression)
        )
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidCronExpression,
                    attributeLocation,
                    values.CronExpression,
                    className
                )
            );
        }

        if (
            values.Priority
            is < SourceGeneratorConstants.NormalJobPriority
                or > SourceGeneratorConstants.LongRunningJobPriority
        )
        {
            diagnostics.Add(
                DiagnosticInfo.Create(DiagnosticDescriptors.InvalidJobPriority, attributeLocation, values.Priority)
            );
        }

        if (values.MaxConcurrency < 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidMaxConcurrency,
                    attributeLocation,
                    values.MaxConcurrency
                )
            );
        }

        if (values.ClusterMaxConcurrency < 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidMaxConcurrency,
                    attributeLocation,
                    values.ClusterMaxConcurrency
                )
            );
        }

        if (
            values.OnMissedRun
            is not null
                and not SourceGeneratorConstants.CoalesceMissedRunPolicy
                and not SourceGeneratorConstants.SkipMissedRunPolicy
        )
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidMissedRunPolicy,
                    attributeLocation,
                    values.OnMissedRun
                )
            );
        }

        if (values.MissedRunGraceSeconds is <= 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidMissedRunGrace,
                    attributeLocation,
                    values.MissedRunGraceSeconds
                )
            );
        }

        if (
            values.OnOverlap
            is not null
                and not SourceGeneratorConstants.AllowOverlapPolicy
                and not SourceGeneratorConstants.SkipOverlapPolicy
        )
        {
            diagnostics.Add(
                DiagnosticInfo.Create(DiagnosticDescriptors.InvalidOverlapPolicy, attributeLocation, values.OnOverlap)
            );
        }
    }

    /// <summary>
    /// Checks that the generated factory <c>static () =&gt; new T()</c> compiles and yields a failure policy: the type
    /// derives from <c>FailurePolicy</c>, is concrete and closed, is visible to generated code, and has a public
    /// parameterless constructor.
    /// </summary>
    public static void ValidateFailurePolicy(
        Compilation compilation,
        ITypeSymbol policy,
        string className,
        Location attributeLocation,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        FailurePolicyType.Validate(
            compilation,
            policy,
            className,
            attributeLocation,
            DiagnosticDescriptors.InvalidFailurePolicy,
            diagnostics
        );
    }
}
