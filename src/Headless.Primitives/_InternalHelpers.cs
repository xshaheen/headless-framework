// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
