// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

/// <summary>Whether code a generator emits into the consuming assembly can name a type.</summary>
internal static class GeneratedCodeAccessibility
{
    /// <summary>
    /// Whether code emitted into the same assembly can name <paramref name="type"/>: every named type in it, its
    /// containing types, and its type arguments are public or internal, and none is file-local.
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
