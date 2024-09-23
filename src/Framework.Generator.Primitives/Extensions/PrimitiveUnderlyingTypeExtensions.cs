using Microsoft.CodeAnalysis;
using Primitives.Generator.Models;

namespace Primitives.Generator.Extensions;

/// <summary>Extension methods for getting the underlying type of primitive.</summary>
internal static class PrimitiveUnderlyingTypeExtensions
{
    /// <summary>Gets the underlying type of primitive based on the given INamedTypeSymbol.</summary>
    /// <param name="type">The INamedTypeSymbol representing the type.</param>
    /// <returns>The PrimitiveUnderlyingType of the given type.</returns>
    public static PrimitiveUnderlyingType GetPrimitiveUnderlyingType(this INamedTypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return PrimitiveUnderlyingType.String;

            case SpecialType.System_Boolean:
                return PrimitiveUnderlyingType.Boolean;

            case SpecialType.System_Char:
                return PrimitiveUnderlyingType.Char;

            case SpecialType.System_SByte:
                return PrimitiveUnderlyingType.SByte;

            case SpecialType.System_Byte:
                return PrimitiveUnderlyingType.Byte;

            case SpecialType.System_Int16:
                return PrimitiveUnderlyingType.Int16;

            case SpecialType.System_UInt16:
                return PrimitiveUnderlyingType.UInt16;

            case SpecialType.System_Int32:
                return PrimitiveUnderlyingType.Int32;

            case SpecialType.System_UInt32:
                return PrimitiveUnderlyingType.UInt32;

            case SpecialType.System_Int64:
                return PrimitiveUnderlyingType.Int64;

            case SpecialType.System_UInt64:
                return PrimitiveUnderlyingType.UInt64;

            case SpecialType.System_Decimal:
                return PrimitiveUnderlyingType.Decimal;

            case SpecialType.System_Single:
                return PrimitiveUnderlyingType.Single;

            case SpecialType.System_Double:
                return PrimitiveUnderlyingType.Double;

            case SpecialType.System_DateTime:
                return PrimitiveUnderlyingType.DateTime;

            default:
                break;
        }

        return type.ToDisplayString() switch
        {
            "System.Guid" => PrimitiveUnderlyingType.Guid,
            "System.DateOnly" => PrimitiveUnderlyingType.DateOnly,
            "System.TimeOnly" => PrimitiveUnderlyingType.TimeOnly,
            "System.TimeSpan" => PrimitiveUnderlyingType.TimeSpan,
            "System.DateTimeOffset" => PrimitiveUnderlyingType.DateTimeOffset,
            _ => PrimitiveUnderlyingType.Other,
        };
    }

    /// <summary>Determines if the given PrimitiveUnderlyingType is numeric.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType to check.</param>
    /// <returns>True if the underlyingType is numeric, false otherwise.</returns>
    public static bool IsNumeric(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType switch
        {
            PrimitiveUnderlyingType.Byte => true,
            PrimitiveUnderlyingType.SByte => true,
            PrimitiveUnderlyingType.Int16 => true,
            PrimitiveUnderlyingType.Int32 => true,
            PrimitiveUnderlyingType.Int64 => true,
            PrimitiveUnderlyingType.UInt16 => true,
            PrimitiveUnderlyingType.UInt32 => true,
            PrimitiveUnderlyingType.UInt64 => true,
            PrimitiveUnderlyingType.Decimal => true,
            PrimitiveUnderlyingType.Double => true,
            PrimitiveUnderlyingType.Single => true,

            _ => false,
        };
    }

    /// <summary>Determines if the given PrimitiveUnderlyingType is a date or time type.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType to check.</param>
    /// <returns>True if the underlyingType is a date or time type, false otherwise.</returns>
    public static bool IsDateOrTime(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType switch
        {
            PrimitiveUnderlyingType.DateTime => true,
            PrimitiveUnderlyingType.DateOnly => true,
            PrimitiveUnderlyingType.TimeOnly => true,
            PrimitiveUnderlyingType.DateTimeOffset => true,
            PrimitiveUnderlyingType.TimeSpan => true,

            _ => false,
        };
    }

    /// <summary>Determines if the given PrimitiveUnderlyingType is a floating point type.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType to check.</param>
    /// <returns>True if the underlying type is a floating point type, false otherwise.</returns>
    public static bool IsFloatingPoint(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType switch
        {
            PrimitiveUnderlyingType.Decimal => true,
            PrimitiveUnderlyingType.Double => true,
            PrimitiveUnderlyingType.Single => true,

            _ => false,
        };
    }

    /// <summary>Determines if the given PrimitiveUnderlyingType is a byte or short.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType to check.</param>
    /// <returns>True if the underlyingType is a byte or short, false otherwise.</returns>
    public static bool IsByteOrShort(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType switch
        {
            PrimitiveUnderlyingType.Byte => true,
            PrimitiveUnderlyingType.SByte => true,
            PrimitiveUnderlyingType.Int16 => true,
            PrimitiveUnderlyingType.UInt16 => true,

            _ => false,
        };
    }

    /// <summary>Gets the default value for the specified PrimitiveUnderlyingType.</summary>
    /// <param name="underlyingType">The PrimitiveUnderlyingType.</param>
    /// <returns>The default value for the specified PrimitiveUnderlyingType.</returns>
    public static object? GetDefaultValue(this PrimitiveUnderlyingType underlyingType)
    {
        return underlyingType switch
        {
            PrimitiveUnderlyingType.String => default(string?),
            PrimitiveUnderlyingType.Boolean => false,
            PrimitiveUnderlyingType.Char => default(char),
            PrimitiveUnderlyingType.Guid => default(Guid),

            PrimitiveUnderlyingType.Byte => default(byte),
            PrimitiveUnderlyingType.SByte => default(sbyte),
            PrimitiveUnderlyingType.Int16 => default(short),
            PrimitiveUnderlyingType.Int32 => default(int),
            PrimitiveUnderlyingType.Int64 => default(long),
            PrimitiveUnderlyingType.UInt16 => default(ushort),
            PrimitiveUnderlyingType.UInt32 => default(uint),
            PrimitiveUnderlyingType.UInt64 => default(ulong),
            PrimitiveUnderlyingType.Decimal => default(decimal),
            PrimitiveUnderlyingType.Double => default(double),
            PrimitiveUnderlyingType.Single => default(float),

            PrimitiveUnderlyingType.DateTime => default(DateTime),
            PrimitiveUnderlyingType.DateOnly => new DateTime(1, 1, 1),
            PrimitiveUnderlyingType.TimeOnly => new DateTime(1, 1, 1, 0, 0, 0),
            PrimitiveUnderlyingType.DateTimeOffset => default(DateTimeOffset),
            PrimitiveUnderlyingType.TimeSpan => default(TimeSpan),
            _ => new DummyValueObject(),
        };
    }

    private readonly struct DummyValueObject;
}
