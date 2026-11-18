// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.LiveQueries.Internal;

/// <summary>The value types a row is matched on, and how a value from a query is brought to a column's type.</summary>
internal static class LiveRowValue
{
    /// <summary>
    /// Returns whether equality on <paramref name="type" /> is the same in memory as in a database: an integer, a
    /// <see cref="bool" />, an enum or a <see cref="Guid" />, nullable or not. Text depends on a collation, and
    /// fractions and dates on a precision.
    /// </summary>
    public static bool IsComparable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsEnum || underlying == typeof(bool) || underlying == typeof(Guid) || IsInteger(underlying);
    }

    public static bool IsInteger(Type type)
    {
        return !type.IsEnum
            && Type.GetTypeCode(type)
                is TypeCode.SByte
                    or TypeCode.Byte
                    or TypeCode.Int16
                    or TypeCode.UInt16
                    or TypeCode.Int32
                    or TypeCode.UInt32
                    or TypeCode.Int64
                    or TypeCode.UInt64;
    }

    /// <summary>
    /// Returns whether every value of <paramref name="from" /> converts to <paramref name="to" /> without loss, so
    /// a comparison made after the conversion says the same as one made before it.
    /// </summary>
    public static bool IsLossless(Type from, Type to)
    {
        from = Nullable.GetUnderlyingType(from) ?? from;
        to = Nullable.GetUnderlyingType(to) ?? to;

        if (from == to)
        {
            return true;
        }

        if (from.IsEnum)
        {
            from = Enum.GetUnderlyingType(from);
        }

        if (!IsInteger(from) || !IsInteger(to))
        {
            return false;
        }

        var (fromBits, fromSigned) = _Width(from);
        var (toBits, toSigned) = _Width(to);

        return fromSigned == toSigned ? fromBits <= toBits : toSigned && fromBits < toBits;
    }

    /// <summary>
    /// Converts a value a query compares a column with to the column's own type. Fails for a value no row of the
    /// column could equal, or whose type says the database holds something other than the entity does.
    /// </summary>
    /// <param name="value">A constant of the query or the value of one of its parameters; not null.</param>
    /// <param name="columnType">The column's CLR type, nullable or not.</param>
    /// <param name="converted">The value as the column's non-nullable type.</param>
    public static bool TryConvert(object value, Type columnType, out object converted)
    {
        var target = Nullable.GetUnderlyingType(columnType) ?? columnType;
        var type = value.GetType();
        converted = value;

        if (type == target)
        {
            return true;
        }

        if (!IsInteger(type))
        {
            return false;
        }

        try
        {
            if (target.IsEnum)
            {
                var number = Convert.ChangeType(value, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture);
                converted = Enum.ToObject(target, number);

                return true;
            }

            if (IsInteger(target))
            {
                converted = Convert.ChangeType(value, target, CultureInfo.InvariantCulture);

                return true;
            }
        }
        catch (OverflowException)
        {
            // Outside the column's range.
        }

        return false;
    }

    private static (int Bits, bool Signed) _Width(Type integer)
    {
        return Type.GetTypeCode(integer) switch
        {
            TypeCode.SByte => (8, true),
            TypeCode.Byte => (8, false),
            TypeCode.Int16 => (16, true),
            TypeCode.UInt16 => (16, false),
            TypeCode.Int32 => (32, true),
            TypeCode.UInt32 => (32, false),
            TypeCode.Int64 => (64, true),
            _ => (64, false),
        };
    }
}
