// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Text;

/// <summary>Extension methods for <see cref="StringBuilder"/>.</summary>
[PublicAPI]
public static class StringBuilderExtensions
{
    /// <summary>Determines whether the contents of the builder start with the given character.</summary>
    /// <param name="stringBuilder">The builder to inspect.</param>
    /// <param name="prefix">The character to look for at the start.</param>
    /// <returns><see langword="true"/> if the builder is non-empty and its first character equals <paramref name="prefix"/>; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stringBuilder"/> is <see langword="null"/>.</exception>
    [JetBrainsPure]
    [SystemPure]
    public static bool StartsWith(this StringBuilder stringBuilder, char prefix)
    {
        Argument.IsNotNull(stringBuilder);

        if (stringBuilder.Length == 0)
        {
            return false;
        }

        return stringBuilder[0] == prefix;
    }

    /// <summary>Determines whether the contents of the builder start with the given string.</summary>
    /// <param name="stringBuilder">The builder to inspect.</param>
    /// <param name="prefix">The string to look for at the start.</param>
    /// <returns><see langword="true"/> if the builder's leading characters equal <paramref name="prefix"/>; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stringBuilder"/> or <paramref name="prefix"/> is <see langword="null"/>.</exception>
    [JetBrainsPure]
    [SystemPure]
    public static bool StartsWith(this StringBuilder stringBuilder, string prefix)
    {
        Argument.IsNotNull(stringBuilder);
        Argument.IsNotNull(prefix);

        if (stringBuilder.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (stringBuilder[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Determines whether the contents of the builder end with the given character.</summary>
    /// <param name="stringBuilder">The builder to inspect.</param>
    /// <param name="suffix">The character to look for at the end.</param>
    /// <returns><see langword="true"/> if the builder is non-empty and its last character equals <paramref name="suffix"/>; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stringBuilder"/> is <see langword="null"/>.</exception>
    [JetBrainsPure]
    [SystemPure]
    public static bool EndsWith(this StringBuilder stringBuilder, char suffix)
    {
        Argument.IsNotNull(stringBuilder);

        if (stringBuilder.Length == 0)
        {
            return false;
        }

        return stringBuilder[^1] == suffix;
    }

    /// <summary>Determines whether the contents of the builder end with the given string.</summary>
    /// <param name="stringBuilder">The builder to inspect.</param>
    /// <param name="suffix">The string to look for at the end.</param>
    /// <returns><see langword="true"/> if the builder's trailing characters equal <paramref name="suffix"/>; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stringBuilder"/> or <paramref name="suffix"/> is <see langword="null"/>.</exception>
    [JetBrainsPure]
    [SystemPure]
    public static bool EndsWith(this StringBuilder stringBuilder, string suffix)
    {
        Argument.IsNotNull(stringBuilder);
        Argument.IsNotNull(suffix);

        if (stringBuilder.Length < suffix.Length)
        {
            return false;
        }

        for (var index = 0; index < suffix.Length; index++)
        {
            if (stringBuilder[stringBuilder.Length - 1 - index] != suffix[suffix.Length - 1 - index])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Removes all leading occurrences of the given character from the builder in place.</summary>
    /// <param name="stringBuilder">The builder to trim.</param>
    /// <param name="trimChar">The character to remove from the start.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stringBuilder"/> is <see langword="null"/>.</exception>
    public static void TrimStart(this StringBuilder stringBuilder, char trimChar)
    {
        Argument.IsNotNull(stringBuilder);

        for (var i = 0; i < stringBuilder.Length; i++)
        {
            if (stringBuilder[i] == trimChar)
            {
                continue;
            }

            if (i > 0)
            {
                stringBuilder.Remove(0, i);
            }

            return;
        }
    }

    /// <summary>Removes all trailing occurrences of the given character from the builder in place.</summary>
    /// <param name="stringBuilder">The builder to trim.</param>
    /// <param name="trimChar">The character to remove from the end.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stringBuilder"/> is <see langword="null"/>.</exception>
    public static void TrimEnd(this StringBuilder stringBuilder, char trimChar)
    {
        Argument.IsNotNull(stringBuilder);

        for (var i = stringBuilder.Length - 1; i >= 0; i--)
        {
            if (stringBuilder[i] == trimChar)
            {
                continue;
            }

            if (i != stringBuilder.Length - 1)
            {
                stringBuilder.Remove(i + 1, stringBuilder.Length - i - 1);
            }

            return;
        }
    }

    /// <summary>Removes all leading and trailing occurrences of the given character from the builder in place.</summary>
    /// <param name="stringBuilder">The builder to trim.</param>
    /// <param name="trimChar">The character to remove from both ends.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stringBuilder"/> is <see langword="null"/>.</exception>
    public static void Trim(this StringBuilder stringBuilder, char trimChar)
    {
        Argument.IsNotNull(stringBuilder);

        stringBuilder.TrimEnd(trimChar);
        stringBuilder.TrimStart(trimChar);
    }

    /// <summary>Appends the invariant-culture string representation of <paramref name="value"/> to the builder.</summary>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="value">The value to append.</param>
    /// <returns>The same <paramref name="sb"/> instance, to allow chaining.</returns>
    public static StringBuilder AppendInvariant(this StringBuilder sb, byte value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Appends the invariant-culture string representation of <paramref name="value"/> to the builder, or nothing if it is <see langword="null"/>.</summary>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="value">The value to append, or <see langword="null"/> to append nothing.</param>
    /// <returns>The same <paramref name="sb"/> instance, to allow chaining.</returns>
    public static StringBuilder AppendInvariant(this StringBuilder sb, byte? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, sbyte value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, sbyte? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, short value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, short? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, ushort value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, ushort? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, int value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, int? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, uint value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, uint? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, long value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, long? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, ulong value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, ulong? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

#if NET6_0_OR_GREATER
    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, Half value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, Half? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }
#endif

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, float value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, float? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, double value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, double? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, decimal value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc cref="AppendInvariant(StringBuilder, byte?)"/>
    public static StringBuilder AppendInvariant(this StringBuilder sb, decimal? value)
    {
        if (value is not null)
        {
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <summary>Appends the invariant-culture formatted representation of <paramref name="value"/> to the builder, or nothing if it is <see langword="null"/>.</summary>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="value">The interpolated string to format and append, or <see langword="null"/> to append nothing.</param>
    /// <returns>The same <paramref name="sb"/> instance, to allow chaining.</returns>
    public static StringBuilder AppendInvariant(this StringBuilder sb, FormattableString? value)
    {
        if (value is not null)
        {
            return sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <summary>Appends the invariant-culture string representation of <paramref name="value"/> to the builder, or nothing if it is <see langword="null"/>.</summary>
    /// <typeparam name="T">A formattable value type.</typeparam>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="value">The value to format and append, or <see langword="null"/> to append nothing.</param>
    /// <returns>The same <paramref name="sb"/> instance, to allow chaining.</returns>
    public static StringBuilder AppendInvariant<T>(this StringBuilder sb, T? value)
        where T : IFormattable
    {
        if (value is not null)
        {
            return sb.Append(value.ToString(format: null, CultureInfo.InvariantCulture));
        }

        return sb;
    }

    /// <summary>Appends the invariant-culture string representation of <paramref name="value"/> to the builder, or nothing if it is <see langword="null"/>.</summary>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="value">The value to append, or <see langword="null"/> to append nothing.</param>
    /// <returns>The same <paramref name="sb"/> instance, to allow chaining.</returns>
    public static StringBuilder AppendInvariant(this StringBuilder sb, object? value)
    {
        if (value is not null)
        {
            return sb.AppendFormat(CultureInfo.InvariantCulture, "{0}", value);
        }

        return sb;
    }

    /// <summary>Appends a composite format string to the builder using <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="format">A composite format string.</param>
    /// <param name="args0">The first object to format and append.</param>
    /// <returns>The same <paramref name="sb"/> instance, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="format"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="format"/> is invalid or an index is out of range for the supplied arguments.</exception>
    public static StringBuilder AppendFormatInvariant(this StringBuilder sb, string format, object? args0)
    {
        return sb.AppendFormat(CultureInfo.InvariantCulture, format, args0);
    }

    /// <inheritdoc cref="AppendFormatInvariant(StringBuilder, string, object?)"/>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="format">A composite format string.</param>
    /// <param name="args0">The first object to format and append.</param>
    /// <param name="args1">The second object to format and append.</param>
    public static StringBuilder AppendFormatInvariant(
        this StringBuilder sb,
        string format,
        object? args0,
        object? args1
    )
    {
        return sb.AppendFormat(CultureInfo.InvariantCulture, format, args0, args1);
    }

    /// <inheritdoc cref="AppendFormatInvariant(StringBuilder, string, object?)"/>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="format">A composite format string.</param>
    /// <param name="args0">The first object to format and append.</param>
    /// <param name="args1">The second object to format and append.</param>
    /// <param name="args2">The third object to format and append.</param>
    public static StringBuilder AppendFormatInvariant(
        this StringBuilder sb,
        string format,
        object? args0,
        object? args1,
        object? args2
    )
    {
        return sb.AppendFormat(CultureInfo.InvariantCulture, format, args0, args1, args2);
    }

    /// <inheritdoc cref="AppendFormatInvariant(StringBuilder, string, object?)"/>
    /// <param name="sb">The builder to append to.</param>
    /// <param name="format">A composite format string.</param>
    /// <param name="args">An array of objects to format and append.</param>
    public static StringBuilder AppendFormatInvariant(this StringBuilder sb, string format, params object?[] args)
    {
        return sb.AppendFormat(CultureInfo.InvariantCulture, format, args);
    }
}
