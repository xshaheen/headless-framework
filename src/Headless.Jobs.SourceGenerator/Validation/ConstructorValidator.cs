using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>
/// Handles validation of constructor configurations for TickerFunction classes.
/// </summary>
internal static class ConstructorValidator
{
    /// <summary>
    /// Validates that a class doesn't have multiple constructors.
    /// Issues a warning if multiple constructors are found and no JobsConstructor attribute is present.
    /// Issues an error if multiple constructors have JobsConstructor attribute.
    /// </summary>
    public static void ValidateMultipleConstructors(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        SourceProductionContext context
    )
    {
        var constructors = classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var hasPrimaryConstructor = classDeclaration.ParameterList?.Parameters.Count > 0;

        // Count total constructors (regular + primary)
        var totalConstructors = constructors.Count + (hasPrimaryConstructor ? 1 : 0);

        // Check for JobsConstructor attributes
        var constructorsWithJobsAttribute = new List<ConstructorDeclarationSyntax>();

        foreach (var constructor in constructors)
        {
            var constructorSymbol = semanticModel.GetDeclaredSymbol(constructor);
            if (constructorSymbol != null)
            {
                var hasJobsAttribute = constructorSymbol
                    .GetAttributes()
                    .Any(attr =>
                    {
                        var attributeClass = attr.AttributeClass;
                        if (attributeClass == null)
                        {
                            return false;
                        }

                        var attributeName = attributeClass.Name;
                        var fullName = attributeClass.ToDisplayString();

                        return string.Equals(attributeName, "JobsConstructorAttribute", StringComparison.Ordinal)
                            || string.Equals(attributeName, "JobsConstructor", StringComparison.Ordinal)
                            || string.Equals(
                                fullName,
                                "Headless.Jobs.Base.JobsConstructorAttribute",
                                StringComparison.Ordinal
                            )
                            || string.Equals(
                                fullName,
                                "Headless.Jobs.Base.JobsConstructor",
                                StringComparison.Ordinal
                            );
                    });

                if (hasJobsAttribute)
                {
                    constructorsWithJobsAttribute.Add(constructor);
                }
            }
        }

        // Error if multiple constructors have JobsConstructor attribute
        if (constructorsWithJobsAttribute.Count > 1)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.MultipleJobsConstructorAttributes,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                )
            );
        }
        // Warning if multiple constructors exist but no JobsConstructor attribute
        else if (totalConstructors > 1 && constructorsWithJobsAttribute.Count == 0)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.MultipleConstructors,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                )
            );
        }
    }
}
