// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives.Models;
using Microsoft.CodeAnalysis;

namespace Framework.Generator.Primitives.Extensions;

/// <summary>Extension methods for getting the underlying type of primitive.</summary>
internal static class PrimitiveUnderlyingTypeExtensions
{
    /// <summary>Gets the underlying type of primitive based on the given INamedTypeSymbol.</summary>
    /// <param name="type">The INamedTypeSymbol representing the type.</param>
    /// <returns>The PrimitiveUnderlyingType of the given type.</returns>
    public static PrimitiveUnderlyingType GetPrimitiveUnderlyingType(this INamedTypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_String => PrimitiveUnderlyingType.String,
            SpecialType.System_Boolean => PrimitiveUnderlyingType.Boolean,
            SpecialType.System_Char => PrimitiveUnderlyingType.Char,
            SpecialType.System_SByte => PrimitiveUnderlyingType.SByte,
            SpecialType.System_Byte => PrimitiveUnderlyingType.Byte,
            SpecialType.System_Int16 => PrimitiveUnderlyingType.Int16,
            SpecialType.System_UInt16 => PrimitiveUnderlyingType.UInt16,
            SpecialType.System_Int32 => PrimitiveUnderlyingType.Int32,
            SpecialType.System_UInt32 => PrimitiveUnderlyingType.UInt32,
            SpecialType.System_Int64 => PrimitiveUnderlyingType.Int64,
            SpecialType.System_UInt64 => PrimitiveUnderlyingType.UInt64,
            SpecialType.System_Decimal => PrimitiveUnderlyingType.Decimal,
            SpecialType.System_Single => PrimitiveUnderlyingType.Single,
            SpecialType.System_Double => PrimitiveUnderlyingType.Double,
            SpecialType.System_DateTime => PrimitiveUnderlyingType.DateTime,
            _ => _GetNonSpecialPrimitiveType(type),
        };
    }

    private static PrimitiveUnderlyingType _GetNonSpecialPrimitiveType(INamedTypeSymbol type)
    {
        var name = type.Name;
        if (!_IsSystemNamespace(type.ContainingNamespace))
        {
            return PrimitiveUnderlyingType.Other;
        }

        return name switch
        {
            "Guid" => PrimitiveUnderlyingType.Guid,
            "DateOnly" => PrimitiveUnderlyingType.DateOnly,
            "TimeOnly" => PrimitiveUnderlyingType.TimeOnly,
            "TimeSpan" => PrimitiveUnderlyingType.TimeSpan,
            "DateTimeOffset" => PrimitiveUnderlyingType.DateTimeOffset,
            _ => PrimitiveUnderlyingType.Other,
        };
    }

    private static bool _IsSystemNamespace(INamespaceSymbol ns) =>
        ns is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };

    /// <summary>Determines if the given PrimitiveUnderlyingType is numeric.</summary>
    /// <param name="type">The PrimitiveUnderlyingType to check.</param>
    /// <returns>True if the underlyingType is numeric, false otherwise.</returns>
    public static bool IsNumeric(this PrimitiveUnderlyingType type)
    {
        return type
            is PrimitiveUnderlyingType.Byte
                or PrimitiveUnderlyingType.SByte
                or PrimitiveUnderlyingType.Int16
                or PrimitiveUnderlyingType.Int32
                or PrimitiveUnderlyingType.Int64
                or PrimitiveUnderlyingType.UInt16
                or PrimitiveUnderlyingType.UInt32
                or PrimitiveUnderlyingType.UInt64
                or PrimitiveUnderlyingType.Decimal
                or PrimitiveUnderlyingType.Double
                or PrimitiveUnderlyingType.Single;
    }

    /// <summary>Determines if the given PrimitiveUnderlyingType is a date or time type.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType to check.</param>
    /// <returns>True if the underlyingType is a date or time type, false otherwise.</returns>
    public static bool IsDateOrTime(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType
            is PrimitiveUnderlyingType.DateTime
                or PrimitiveUnderlyingType.DateOnly
                or PrimitiveUnderlyingType.TimeOnly
                or PrimitiveUnderlyingType.DateTimeOffset
                or PrimitiveUnderlyingType.TimeSpan;
    }

    /// <summary>Determines if the given PrimitiveUnderlyingType is a floating point type.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType to check.</param>
    /// <returns>True if the underlying type is a floating point type, false otherwise.</returns>
    public static bool IsFloatingPoint(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType
            is PrimitiveUnderlyingType.Decimal
                or PrimitiveUnderlyingType.Double
                or PrimitiveUnderlyingType.Single;
    }

    /// <summary>Determines if the given PrimitiveUnderlyingType is a byte or short.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType to check.</param>
    /// <returns>True if the underlyingType is a byte or short, false otherwise.</returns>
    public static bool IsByteOrShort(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType
            is PrimitiveUnderlyingType.Byte
                or PrimitiveUnderlyingType.SByte
                or PrimitiveUnderlyingType.Int16
                or PrimitiveUnderlyingType.UInt16;
    }

    /// <summary>Gets the default value for the specified PrimitiveUnderlyingType.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType.</param>
    /// <returns>The default value for the specified PrimitiveUnderlyingType.</returns>
    public static object? GetDefaultValue(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType switch
        {
            PrimitiveUnderlyingType.String => null,
            PrimitiveUnderlyingType.Boolean => false,
            PrimitiveUnderlyingType.Char => '\0',
            PrimitiveUnderlyingType.Guid => Guid.Empty,

            PrimitiveUnderlyingType.Byte => 0,
            PrimitiveUnderlyingType.SByte => 0,
            PrimitiveUnderlyingType.Int16 => 0,
            PrimitiveUnderlyingType.Int32 => 0,
            PrimitiveUnderlyingType.Int64 => 0,
            PrimitiveUnderlyingType.UInt16 => 0,
            PrimitiveUnderlyingType.UInt32 => 0,
            PrimitiveUnderlyingType.UInt64 => 0,
            PrimitiveUnderlyingType.Decimal => 0,
            PrimitiveUnderlyingType.Double => 0,
            PrimitiveUnderlyingType.Single => 0,

            PrimitiveUnderlyingType.DateTime => default(DateTime),
            PrimitiveUnderlyingType.DateOnly => new DateTime(1, 1, 1),
            PrimitiveUnderlyingType.TimeOnly => new DateTime(1, 1, 1, 0, 0, 0),
            PrimitiveUnderlyingType.DateTimeOffset => default(DateTimeOffset),
            PrimitiveUnderlyingType.TimeSpan => TimeSpan.Zero,
            _ => new DummyValueObject(),
        };
    }

    private readonly struct DummyValueObject;
}
