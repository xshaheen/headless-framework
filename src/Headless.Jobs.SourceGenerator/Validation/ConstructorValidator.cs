// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator.Utilities;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>
/// Handles validation of constructor configurations for JobFunction classes.
/// </summary>
internal static class ConstructorValidator
{
    /// <summary>
    /// Warns when a class has several constructors and none is marked <c>[JobsConstructor]</c>, and errors when more
    /// than one is marked.
    /// </summary>
    public static void ValidateMultipleConstructors(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        var constructors = classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var hasPrimaryConstructor = classDeclaration.ParameterList?.Parameters.Count > 0;
        var totalConstructors = constructors.Count + (hasPrimaryConstructor ? 1 : 0);
        var markedConstructors = constructors.Count(constructor =>
            semanticModel.GetDeclaredSymbol(constructor) is { } symbol
            && symbol.GetAttributes().Any(SourceGeneratorUtilities.IsJobsConstructorAttribute)
        );

        if (markedConstructors > 1)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MultipleJobsConstructorAttributes,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                )
            );
        }
        else if (totalConstructors > 1 && markedConstructors == 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MultipleConstructors,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                )
            );
        }
    }
}
