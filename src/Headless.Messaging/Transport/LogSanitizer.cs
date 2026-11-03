// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>
/// Sanitizes values originating from message broker wire data (transport headers, message
/// metadata) before they are interpolated into structured log parameters. This is the only
/// layer in the framework where untrusted external strings enter log output — publisher
/// paths, configuration, and storage operations log trusted application data and do not
/// need sanitization.
/// <para>
/// Strips control characters (which enable log-line forging in text-based log sinks),
/// Unicode line/paragraph separators U+2028/U+2029 (which cause line-splitting in
/// structured log sinks), lone surrogates U+D800–U+DFFF (invalid UTF-16 that can corrupt
/// log output), and Unicode bidi overrides U+202A–U+202E / U+2066–U+2069 (which can
/// visually reorder text in log viewers to hide malicious content).
/// </para>
/// <para>
/// A transport passes every broker-supplied value it logs or puts in an exception message through
/// <see cref="Sanitize"/>.
/// </para>
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Sanitizes a string value to prevent log injection.
    /// Strips control characters, Unicode line/paragraph separators (U+2028, U+2029),
    /// lone surrogates (U+D800-U+DFFF), and Unicode bidi overrides (U+202A-U+202E, U+2066-U+2069).
    /// Optionally truncates to <paramref name="maxLength"/> characters (appending "..." when truncated).
    /// Returns null if input is null.
    /// </summary>
    /// <param name="value">The broker-supplied value.</param>
    /// <param name="maxLength">The longest result before the truncation suffix; unlimited when omitted.</param>
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
