// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator.Parsing;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>
/// Handles validation of constructor configurations for JobFunction classes.
/// </summary>
internal static class ConstructorValidator
{
    /// <summary>
    /// Warns when a class has several constructors and none is marked <c>[JobsConstructor]</c>, and errors when more
    /// than one is marked. Counts span every part of a partial class.
    /// </summary>
    public static void ValidateMultipleConstructors(
        ConstructorSelection constructors,
        SyntaxToken classIdentifier,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        if (constructors.MarkedCount > 1)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MultipleJobsConstructorAttributes,
                    classIdentifier.GetLocation(),
                    classIdentifier.Text
                )
            );
        }
        else if (constructors.DeclaredCount > 1 && constructors.MarkedCount == 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MultipleConstructors,
                    classIdentifier.GetLocation(),
                    classIdentifier.Text
                )
            );
        }
    }
}
