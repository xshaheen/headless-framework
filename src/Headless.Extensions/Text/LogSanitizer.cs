// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless;

/// <summary>
/// Sanitizes an untrusted string, such as a request header or a message broker's wire data, before it becomes a log
/// property or part of an exception message.
/// <para>
/// Strips control characters (which enable log-line forging in text sinks), the Unicode line and paragraph
/// separators U+2028 and U+2029 (which split lines in structured sinks), every UTF-16 surrogate code unit
/// U+D800–U+DFFF (which removes lone surrogates and also every character outside the Basic Multilingual Plane, such
/// as emoji), and the Unicode bidi overrides and isolates U+202A–U+202E and U+2066–U+2069 (which visually reorder
/// text in log viewers to hide content).
/// </para>
/// <para>
/// Only the ESC character of an ANSI escape sequence is stripped; the printable rest of the sequence stays as inert
/// text.
/// </para>
/// </summary>
[PublicAPI]
public static class LogSanitizer
{
    /// <summary>
    /// Strips the characters listed on <see cref="LogSanitizer"/>. A value longer than <paramref name="maxLength"/>
    /// before stripping is cut and ends in a "..." suffix that counts toward <paramref name="maxLength"/>, even when
    /// stripping alone would have made it fit; with a <paramref name="maxLength"/> of 3 or less it becomes "..." alone.
    /// A value that needs neither step is returned as the same instance.
    /// </summary>
    /// <param name="value">The untrusted value.</param>
    /// <param name="maxLength">The longest result, the truncation suffix included; unlimited when omitted.</param>
    /// <returns>The sanitized value, or <see langword="null"/> when <paramref name="value"/> is.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is negative.</exception>
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Sanitize(string? value, int maxLength = int.MaxValue)
    {
        Argument.IsPositiveOrZero(maxLength);

        if (value is null)
        {
            return null;
        }

        const string truncationSuffix = "...";
        var needsTruncation = value.Length > maxLength;
        var scanLength = needsTruncation ? maxLength : value.Length;

        var needsSanitization = false;
        for (var i = 0; i < scanLength; i++)
        {
            if (_ShouldStrip(value[i]))
            {
                needsSanitization = true;
                break;
            }
        }

        if (!needsSanitization && !needsTruncation)
        {
            return value;
        }

        if (!needsSanitization && needsTruncation)
        {
            var truncLen = Math.Max(maxLength - truncationSuffix.Length, 0);
            return string.Concat(value.AsSpan(0, truncLen), truncationSuffix);
        }

        var effectiveMax = needsTruncation ? Math.Max(maxLength - truncationSuffix.Length, 0) : scanLength;
        var buffer = new char[effectiveMax + truncationSuffix.Length];
        var pos = 0;
        var src = 0;

        for (; src < scanLength && pos < effectiveMax; src++)
        {
            var c = value[src];
            if (!_ShouldStrip(c))
            {
                buffer[pos++] = c;
            }
        }

        // Append '...' only when we genuinely didn't consume the entire input.
        // When needsTruncation is true but stripped characters are dense, the
        // sanitized output may fit within the limit — don't mislead with '...'.
        var wasTruncated = src < value.Length;
        if (wasTruncated)
        {
            truncationSuffix.AsSpan().CopyTo(buffer.AsSpan(pos));
            pos += truncationSuffix.Length;
        }

        return new string(buffer, 0, pos);
    }

    /// <summary>
    /// Returns true if the character should be stripped from log output.
    /// </summary>
    private static bool _ShouldStrip(char c)
    {
        return char.IsControl(c)
            || c
                is '\u2028' // Line Separator
                    or '\u2029' // Paragraph Separator
                    or (>= '\uD800' and <= '\uDFFF') // Lone surrogates
                    or (>= '\u202A' and <= '\u202E') // Bidi overrides
                    or (>= '\u2066' and <= '\u2069'); // Bidi isolates
    }
}
