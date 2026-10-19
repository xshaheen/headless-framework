// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives.Helpers;
using Headless.Generator.Primitives.Models;
using Microsoft.CodeAnalysis;

namespace Headless.Generator.Primitives.Extensions;

/// <summary>Extension methods for working with Roslyn's Compilation and related types.</summary>
internal static class CompilationExtensions
{
    /// <summary>Checks if the given named type symbol implements the IPrimitive interface.</summary>
    /// <param name="x">The named type symbol to check.</param>
    /// <returns>True if the type implements the IPrimitive interface; otherwise, false.</returns>
    public static bool IsImplementIPrimitive(this INamedTypeSymbol x)
    {
        if (!x.IsGenericType || !string.Equals(x.Name, AbstractionConstants.Interface, StringComparison.Ordinal))
        {
            return false;
        }

        // Compare namespace parts directly (cheap) instead of ToDisplayString (expensive)
        var ns = x.ContainingNamespace;
        return ns
            is {
                Name: AbstractionConstants.NamespacePart3,
                ContainingNamespace:
                {
                    Name: AbstractionConstants.NamespacePart2,
                    ContainingNamespace:
                    { Name: AbstractionConstants.NamespacePart1, ContainingNamespace.IsGlobalNamespace: true },
                },
            };
    }

    /// <summary>
    /// Gets the underlying primitive type information associated with the specified named type symbol.
    /// </summary>
    /// <param name="type">The named type symbol for which to retrieve the underlying primitive type information.</param>
    /// <param name="domainTypes">A list of named type symbols representing domain types to track recursion.</param>
    /// <returns>A tuple containing the <see cref="PrimitiveUnderlyingType"/> enum value representing the primitive type and the corresponding named type symbol.</returns>
    public static (PrimitiveUnderlyingType underlyingType, INamedTypeSymbol typeSymbol) GetUnderlyingPrimitiveType(
        this INamedTypeSymbol type,
        List<INamedTypeSymbol> domainTypes
    )
    {
        while (true)
        {
            var underlyingType = type.GetPrimitiveUnderlyingType();

            if (underlyingType != PrimitiveUnderlyingType.Other)
            {
                return (underlyingType, type);
            }

            var domainType = type.Interfaces.FirstOrDefault(x => x.IsImplementIPrimitive());

            if (domainType is null)
            {
                return (PrimitiveUnderlyingType.Other, type);
            }

            // Recurse into the domain type
            if (domainType.TypeArguments[0] is not INamedTypeSymbol primitiveType)
            {
                throw new InvalidOperationException("primitiveType is null");
            }

            domainTypes.Add(type);
            type = primitiveType;
        }
    }

    public static string? GetStringSyntaxAttribute(this INamedTypeSymbol primitiveType)
    {
        var underlyingType = primitiveType.GetPrimitiveUnderlyingType();

        return underlyingType.GetStringSyntaxAttribute();
    }

    /// <summary>Gets the StringSyntax attribute for a given PrimitiveUnderlyingType.</summary>
    /// <param name="underlyingType">The underlying type enum.</param>
    /// <returns>The StringSyntax attribute string or null.</returns>
    public static string? GetStringSyntaxAttribute(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType switch
        {
            PrimitiveUnderlyingType.String =>
                "[global::System.Diagnostics.CodeAnalysis.StringSyntax(global::System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.CompositeFormat)]",
            PrimitiveUnderlyingType.Guid =>
                "[global::System.Diagnostics.CodeAnalysis.StringSyntax(global::System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.GuidFormat)]",
            PrimitiveUnderlyingType.TimeSpan =>
                "[global::System.Diagnostics.CodeAnalysis.StringSyntax(global::System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.TimeSpanFormat)]",
            PrimitiveUnderlyingType.TimeOnly =>
                "[global::System.Diagnostics.CodeAnalysis.StringSyntax(global::System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.TimeOnlyFormat)]",
            PrimitiveUnderlyingType.DateOnly =>
                "[global::System.Diagnostics.CodeAnalysis.StringSyntax(global::System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.DateOnlyFormat)]",
            PrimitiveUnderlyingType.DateTime =>
                "[global::System.Diagnostics.CodeAnalysis.StringSyntax(global::System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.DateTimeFormat)]",
            PrimitiveUnderlyingType.DateTimeOffset =>
                "[global::System.Diagnostics.CodeAnalysis.StringSyntax(global::System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.DateTimeFormat)]",
            PrimitiveUnderlyingType.Byte
            or PrimitiveUnderlyingType.SByte
            or PrimitiveUnderlyingType.Int16
            or PrimitiveUnderlyingType.UInt16
            or PrimitiveUnderlyingType.Int32
            or PrimitiveUnderlyingType.UInt32
            or PrimitiveUnderlyingType.Int64
            or PrimitiveUnderlyingType.UInt64
            or PrimitiveUnderlyingType.Decimal
            or PrimitiveUnderlyingType.Single
            or PrimitiveUnderlyingType.Double =>
                "[global::System.Diagnostics.CodeAnalysis.StringSyntax(global::System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.NumericFormat)]",

            _ => null,
        };
    }
}
