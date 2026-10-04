// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace Headless.Generator.Primitives;

/// <summary>Provides extension methods for converting values to XML string representations.</summary>
/// <remarks>Internal generator-emitted plumbing. Kept public for generated code execution.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ToXmlStringExtensions
{
    /// <summary>
    /// Converts a <see cref="DateTime" /> value to an XML string representation formatted as "yyyy-MM-ddTHH:mm:sszzz".
    /// </summary>
    /// <param name="value">The date and time value to convert.</param>
    /// <returns>The formatted XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this DateTime value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts a <see cref="DateOnly" /> value to an XML string representation formatted as "yyyy-MM-dd".
    /// </summary>
    /// <param name="value">The date value to convert.</param>
    /// <returns>The formatted XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts a <see cref="TimeOnly" /> value to an XML string representation formatted as "HH:mm:sszzz".
    /// </summary>
    /// <param name="value">The time value to convert.</param>
    /// <returns>The formatted XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this TimeOnly value)
    {
        return value.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts a <see cref="DateTimeOffset" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The date and time offset to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this DateTimeOffset value)
    {
        return XmlConvert.ToString(value);
    }

    /// <summary>
    /// Converts a <see cref="TimeSpan" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The time span to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this TimeSpan value)
    {
        return XmlConvert.ToString(value);
    }

    /// <summary>
    /// Converts a <see cref="byte" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The byte to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this byte value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="sbyte" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The signed byte to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this sbyte value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="short" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The short integer to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this short value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="ushort" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The unsigned short integer to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this ushort value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="int" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this int value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="uint" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The unsigned integer to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this uint value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="long" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The 64-bit integer to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this long value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="ulong" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The unsigned 64-bit integer to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this ulong value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="float" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The floating-point value to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this float value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="double" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The double-precision value to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this double value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="decimal" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The decimal value to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this decimal value)
    {
        return value.ToString(format: null, NumberFormatInfo.InvariantInfo);
    }

    /// <summary>
    /// Converts a <see cref="Guid" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The identifier value to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this Guid value)
    {
        return value.ToString();
    }

    /// <summary>
    /// Converts a <see cref="bool" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this bool value)
    {
        return value ? "true" : "false";
    }

    /// <summary>
    /// Converts a <see cref="char" /> value to an XML string representation.
    /// </summary>
    /// <param name="value">The character value to convert.</param>
    /// <returns>The XML string representation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToXmlString(this char value)
    {
        return value.ToString();
    }
}
