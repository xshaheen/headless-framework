// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Headless.Primitives;

/// <summary>
/// Internal copies of the small <c>Headless.Extensions</c> helpers used by this package. They are duplicated here
/// (rather than referenced) so the dependency direction stays <c>Headless.Extensions → Headless.Primitives</c> and
/// never the reverse. Keep these in sync with their originals in <c>Headless.Extensions</c> if the logic changes.
/// </summary>
internal static class TypePrimitiveHelpers
{
    /// <summary>Determines whether the type is a closed <see cref="Nullable{T}"/> value type.</summary>
    public static bool IsNullableValueType(this Type type)
    {
        return type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    /// <summary>
    /// Determines whether the type is a primitive, an enum (when <paramref name="includeEnums"/> is set), or one of
    /// <see cref="string"/>, <see cref="decimal"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>,
    /// <see cref="TimeSpan"/>, or <see cref="Guid"/> (optionally wrapped in <see cref="Nullable{T}"/>).
    /// </summary>
    public static bool IsPrimitiveExtended(this Type type, bool includeNullables = true, bool includeEnums = false)
    {
        if (_IsPrimitive(type, includeEnums))
        {
            return true;
        }

        if (includeNullables && type.IsNullableValueType() && type.GenericTypeArguments.Length != 0)
        {
            return _IsPrimitive(type.GenericTypeArguments[0], includeEnums);
        }

        return false;

        static bool _IsPrimitive(Type type, bool includeEnums)
        {
            if (type.IsPrimitive)
            {
                return true;
            }

            if (includeEnums && type.IsEnum)
            {
                return true;
            }

            return type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);
        }
    }
}

/// <summary>
/// Internal copy of <c>Headless.Text.LookupNormalizer.NormalizePhoneNumber</c>. Duplicated to keep the dependency
/// direction one-way (see <see cref="TypePrimitiveHelpers"/>).
/// </summary>
internal static class PhoneNumberNormalizer
{
    /// <summary>
    /// Normalizes a phone number by trimming whitespace, removing spaces, converting digits to their invariant
    /// (ASCII) form, and stripping a single trailing <c>0</c>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(number))]
    public static string? NormalizePhoneNumber(string? number)
    {
        var trimmed = string.IsNullOrWhiteSpace(number) ? null : number.Trim();

        if (trimmed is null)
        {
            return null;
        }

        var withoutSpaces = trimmed.Contains(' ', StringComparison.Ordinal)
            ? trimmed.Replace(" ", "", StringComparison.Ordinal)
            : trimmed;

        var builder = new StringBuilder(withoutSpaces.Length);

        foreach (var c in withoutSpaces)
        {
            // Map any Unicode decimal digit to its ASCII equivalent without allocating or touching culture.
            builder.Append(char.IsDigit(c) ? (char)('0' + (int)char.GetNumericValue(c)) : c);
        }

        var digits = builder.ToString();

        return digits.EndsWith('0') ? digits[..^1] : digits;
    }
}
