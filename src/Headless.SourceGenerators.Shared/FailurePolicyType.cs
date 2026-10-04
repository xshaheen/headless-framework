// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

/// <summary>
/// Validation rules for failure policy types specified on handler declarations.
/// </summary>
internal static class FailurePolicyType
{
    public const string MetadataName = "Headless.Reliability.FailurePolicy";

    /// <summary>
    /// Determines whether the specified policy type derives from the failure policy base class,
    /// is non-abstract, is accessible to generated code, and exposes a public parameterless constructor.
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
