// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Headless.Checks;

/// <summary>
/// Builds the custom message of <see cref="Ensure.True(bool, TrueCheckInterpolatedStringHandler, string?)"/> and
/// <see cref="Argument.IsTrue(bool, TrueCheckInterpolatedStringHandler, string?)"/> only when the condition is
/// <see langword="false"/>, so a passing check formats and allocates nothing.
/// </summary>
/// <remarks>
/// The compiler creates this handler when an interpolated string with formatted holes is passed as the message;
/// string literals and <see cref="string"/> variables still bind to the <see cref="string"/> overload. The message is
/// formatted with <see cref="CultureInfo.InvariantCulture"/>, like the default messages of <see cref="Argument"/>
/// and <see cref="Ensure"/>, so it does not change with the thread culture.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
[InterpolatedStringHandler]
public ref struct TrueCheckInterpolatedStringHandler
{
    private DefaultInterpolatedStringHandler _builder;

    /// <summary>Creates a handler that appends only when <paramref name="condition"/> is <see langword="false"/>.</summary>
    /// <param name="literalLength">The number of constant characters in the interpolated string.</param>
    /// <param name="formattedCount">The number of interpolation holes in the interpolated string.</param>
    /// <param name="condition">The checked condition; the message is built only when it is <see langword="false"/>.</param>
    /// <param name="shouldAppend">Whether the compiler should append the interpolated string's parts.</param>
    public TrueCheckInterpolatedStringHandler(
        int literalLength,
        int formattedCount,
        bool condition,
        out bool shouldAppend
    )
    {
        if (condition)
        {
            _builder = default;
            shouldAppend = false;
        }
        else
        {
            _builder = new DefaultInterpolatedStringHandler(
                literalLength,
                formattedCount,
                CultureInfo.InvariantCulture
            );
            shouldAppend = true;
        }
    }

    /// <summary>Writes the specified string to the handler.</summary>
    /// <param name="value">The string to write.</param>
    public void AppendLiteral(string value) => _builder.AppendLiteral(value);

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    public void AppendFormatted<T>(T value) => _builder.AppendFormatted(value);

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="format">The format string.</param>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    public void AppendFormatted<T>(T value, string? format) => _builder.AppendFormatted(value, format);

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="alignment">Minimum number of characters to write; a negative value left-aligns.</param>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    public void AppendFormatted<T>(T value, int alignment) => _builder.AppendFormatted(value, alignment);

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="alignment">Minimum number of characters to write; a negative value left-aligns.</param>
    /// <param name="format">The format string.</param>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    public void AppendFormatted<T>(T value, int alignment, string? format) =>
        _builder.AppendFormatted(value, alignment, format);

    /// <summary>Writes the specified character span to the handler.</summary>
    /// <param name="value">The span to write.</param>
    public void AppendFormatted(scoped ReadOnlySpan<char> value) => _builder.AppendFormatted(value);

    /// <summary>Writes the specified character span to the handler.</summary>
    /// <param name="value">The span to write.</param>
    /// <param name="alignment">Minimum number of characters to write; a negative value left-aligns.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(scoped ReadOnlySpan<char> value, int alignment = 0, string? format = null) =>
        _builder.AppendFormatted(value, alignment, format);

    /// <summary>Writes the specified string to the handler.</summary>
    /// <param name="value">The string to write.</param>
    public void AppendFormatted(string? value) => _builder.AppendFormatted(value);

    /// <summary>Writes the specified string to the handler.</summary>
    /// <param name="value">The string to write.</param>
    /// <param name="alignment">Minimum number of characters to write; a negative value left-aligns.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(string? value, int alignment = 0, string? format = null) =>
        _builder.AppendFormatted(value, alignment, format);

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="alignment">Minimum number of characters to write; a negative value left-aligns.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(object? value, int alignment = 0, string? format = null) =>
        _builder.AppendFormatted(value, alignment, format);

    /// <summary>Returns the built message and returns the handler's pooled buffer.</summary>
    internal string ToStringAndClear() => _builder.ToStringAndClear();
}
