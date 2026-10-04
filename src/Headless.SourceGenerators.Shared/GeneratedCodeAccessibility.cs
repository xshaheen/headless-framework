// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

/// <summary>Checks whether generated code in the consuming assembly can reference a given type.</summary>
internal static class GeneratedCodeAccessibility
{
    /// <summary>
    /// Determines whether the specified type, its containing types, and its generic type arguments
    /// are accessible to generated code and not file-local.
    /// </summary>
    public static bool IsAccessible(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsAccessible(array.ElementType);
            case INamedTypeSymbol named:
                for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
                {
                    if (
                        current.IsFileLocal
                        || current.DeclaredAccessibility
                            is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
                    )
                    {
                        return false;
                    }

                    if (current.TypeArguments.Any(argument => !IsAccessible(argument)))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }
}
