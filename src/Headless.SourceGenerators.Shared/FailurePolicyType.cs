// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

/// <summary>
/// The rule a <c>FailurePolicy</c> type named on a handler attribute must meet, shared by every generator that emits a
/// policy factory so Messaging and Jobs accept and reject the same types.
/// </summary>
internal static class FailurePolicyType
{
    public const string MetadataName = "Headless.Reliability.FailurePolicy";

    /// <summary>
    /// Whether the generated factory <c>static () =&gt; new T()</c> compiles and yields a failure policy: the type
    /// derives from <c>FailurePolicy</c>, is concrete and closed, is visible to generated code, and has a public
    /// parameterless constructor.
    /// </summary>
    public static bool IsValid(Compilation compilation, ITypeSymbol policy)
    {
        var policyBase = compilation.GetTypeByMetadataName(MetadataName);

        return policyBase is not null
            && policy
                is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false, IsUnboundGenericType: false } named
            && !_ContainsTypeParameter(named)
            && _DerivesFrom(named, policyBase)
            && GeneratedCodeAccessibility.IsAccessible(named)
            && compilation.IsSymbolAccessibleWithin(named, compilation.Assembly)
            && named.InstanceConstructors.Any(constructor =>
                constructor.Parameters.IsEmpty && constructor.DeclaredAccessibility == Accessibility.Public
            );
    }

    /// <summary>
    /// Reports <paramref name="descriptor"/> at <paramref name="attributeLocation"/>, with the policy's display name and
    /// <paramref name="className"/> as message arguments, when <paramref name="policy"/> fails <see cref="IsValid"/>.
    /// </summary>
    public static void Validate(
        Compilation compilation,
        ITypeSymbol policy,
        string className,
        Location attributeLocation,
        DiagnosticDescriptor descriptor,
        ICollection<DiagnosticInfo> diagnostics
    )
    {
        if (!IsValid(compilation, policy))
        {
            diagnostics.Add(DiagnosticInfo.Create(descriptor, attributeLocation, policy.ToDisplayString(), className));
        }
    }

    private static bool _DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the type, or a type containing it, still has an open type parameter the factory cannot bind.</summary>
    private static bool _ContainsTypeParameter(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter))
            {
                return true;
            }
        }

        return false;
    }
}
