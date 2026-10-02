// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.SourceGenerator.Utilities;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;

namespace Headless.Messaging.SourceGenerator.Validation;

/// <summary>
/// The per-class consumer rules. Rules that compare consumers with each other run after collection.
/// </summary>
internal static class ConsumerValidator
{
    /// <summary>Checks that generated code can see and construct the class.</summary>
    /// <remarks>
    /// Accessibility is read from the symbol, not the modifiers of one declaration: a top-level class without a modifier
    /// is internal, and a partial class may declare its accessibility on another part. A nested class is accepted when
    /// every type that contains it is visible to the assembly, because the generated file names it fully qualified.
    /// </remarks>
    public static void ValidateClass(
        INamedTypeSymbol classSymbol,
        SyntaxToken classIdentifier,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        var location = classIdentifier.GetLocation();
        if (!GeneratedCodeAccessibility.IsAccessible(classSymbol))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InaccessibleConsumer,
                    location,
                    classSymbol.ToDisplayString(),
                    classIdentifier.Text
                )
            );
        }

        // IsGenericType is also true for a class nested in a generic type, which the dispatcher could not close either.
        if (classSymbol.IsAbstract || classSymbol.IsGenericType)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(DiagnosticDescriptors.AbstractOrGenericConsumer, location, classIdentifier.Text)
            );
        }
    }

    /// <summary>Checks the attribute's own values: the identity form.</summary>
    public static void ValidateAttribute(
        string? identity,
        string className,
        Location attributeLocation,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        // An identity that is not a compile-time constant is already a compiler error at the attribute, and arrives
        // here as null, so the rule names both causes.
        if (!HandlerIdentity.IsValid(identity))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidConsumerIdentity,
                    attributeLocation,
                    identity ?? "null",
                    className
                )
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
        if (!FailurePolicyType.IsValid(compilation, policy))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidFailurePolicy,
                    attributeLocation,
                    policy.ToDisplayString(),
                    className
                )
            );
        }
    }
}
