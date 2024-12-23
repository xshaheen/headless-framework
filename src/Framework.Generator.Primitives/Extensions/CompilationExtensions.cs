// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives.Helpers;
using Framework.Generator.Primitives.Models;
using Microsoft.CodeAnalysis;

namespace Framework.Generator.Primitives.Extensions;

/// <summary>Extension methods for working with Roslyn's Compilation and related types.</summary>
internal static class CompilationExtensions
{
    /// <summary>Checks if the given named type symbol implements the IPrimitive interface.</summary>
    /// <param name="x">The named type symbol to check.</param>
    /// <returns>True if the type implements the IPrimitive interface; otherwise, false.</returns>
    public static bool IsImplementIPrimitive(this INamedTypeSymbol x)
    {
        return x is { IsGenericType: true, Name: AbstractionConstants.Interface }
            && string.Equals(
                x.ContainingNamespace.ToDisplayString(),
                AbstractionConstants.Namespace,
                StringComparison.Ordinal
            );
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

    /// <summary>Gets the Swagger type and format for a given primitive type.</summary>
    /// <param name="primitiveType">The named type symbol representing the primitive type.</param>
    /// <returns>A tuple containing the Swagger type and format as strings.</returns>
    public static (string type, string format) GetSwashbuckleSwaggerTypeAndFormat(this INamedTypeSymbol primitiveType)
    {
        var underlyingType = primitiveType.GetPrimitiveUnderlyingType();

        if (underlyingType.IsNumeric())
        {
            var format = underlyingType.ToString();

            return underlyingType.IsFloatingPoint()
                ? ("number", format.ToLowerInvariant())
                : ("integer", format.ToLowerInvariant());
        }

        return underlyingType switch
        {
            PrimitiveUnderlyingType.Boolean => ("boolean", ""),
            PrimitiveUnderlyingType.Guid => ("string", "uuid"),
            PrimitiveUnderlyingType.Char => ("string", ""),

            PrimitiveUnderlyingType.DateTime => ("string", "date-time"),
            PrimitiveUnderlyingType.DateOnly => ("date", "yyyy-MM-dd"),
            PrimitiveUnderlyingType.TimeOnly => ("string", "HH:mm:ss"),
            PrimitiveUnderlyingType.DateTimeOffset => ("string", "date-time"),
            PrimitiveUnderlyingType.TimeSpan => ("integer", "int64"),

            _ => ("string", ""),
        };
    }

    /// <summary>Gets the Swagger type and format for a given primitive type.</summary>
    /// <param name="primitiveType">The named type symbol representing the primitive type.</param>
    /// <returns>A tuple containing the Swagger type and format as strings.</returns>
    public static (string Type, string? Format) GetNswagSwaggerTypeAndFormatAndExample(
        this INamedTypeSymbol primitiveType
    )
    {
        var underlyingType = primitiveType.GetPrimitiveUnderlyingType();

        const string stringType = "JsonObjectType.String";
        const string booleanPointType = "JsonObjectType.Boolean";
        const string integerType = "JsonObjectType.Integer";
        const string floatingPointType = "JsonObjectType.Number";

        return underlyingType switch
        {
            PrimitiveUnderlyingType.Boolean => (booleanPointType, null),
            PrimitiveUnderlyingType.Char => (stringType, null),
            PrimitiveUnderlyingType.String => (stringType, "\"string\""),
            PrimitiveUnderlyingType.Guid => (stringType, "JsonFormatStrings.Guid"),
            PrimitiveUnderlyingType.SByte => (integerType, "JsonFormatStrings.Byte"),
            PrimitiveUnderlyingType.Byte => (integerType, "JsonFormatStrings.Byte"),
            PrimitiveUnderlyingType.Int16 => (integerType, "\"int16\""),
            PrimitiveUnderlyingType.UInt16 => (integerType, "\"uint16\""),
            PrimitiveUnderlyingType.Int32 => (integerType, "JsonFormatStrings.Integer"),
            PrimitiveUnderlyingType.UInt32 => (integerType, "\"uint32\""),
            PrimitiveUnderlyingType.Int64 => (integerType, "JsonFormatStrings.Long"),
            PrimitiveUnderlyingType.UInt64 => (integerType, "JsonFormatStrings.ULong"),
            PrimitiveUnderlyingType.Decimal => (floatingPointType, "JsonFormatStrings.Decimal"),
            PrimitiveUnderlyingType.Single => (floatingPointType, "JsonFormatStrings.Float"),
            PrimitiveUnderlyingType.Double => (floatingPointType, "JsonFormatStrings.Double"),
            PrimitiveUnderlyingType.DateTimeOffset => (stringType, "JsonFormatStrings.DateTime"),
            PrimitiveUnderlyingType.DateTime => (stringType, "JsonFormatStrings.DateTime"),
            PrimitiveUnderlyingType.DateOnly => (stringType, "JsonFormatStrings.Date"),
            PrimitiveUnderlyingType.TimeOnly => (stringType, "JsonFormatStrings.Time"),
            PrimitiveUnderlyingType.TimeSpan => (stringType, "JsonFormatStrings.TimeSpan"),
            PrimitiveUnderlyingType.Other or _ => (stringType, null),
        };
    }

    public static string? GetStringSyntaxAttribute(this INamedTypeSymbol primitiveType)
    {
        var underlyingType = primitiveType.GetPrimitiveUnderlyingType();

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
