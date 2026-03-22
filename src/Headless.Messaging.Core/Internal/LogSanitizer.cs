// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Shared sanitization utilities for log-safe string values.
/// Strips control characters and Unicode bidi overrides to prevent log injection.
/// </summary>
internal static class LogSanitizer
{
    /// <summary>
    /// Sanitizes a string value to prevent log injection.
    /// Strips control characters and Unicode bidi overrides (U+202A-U+202E, U+2066-U+2069).
    /// Returns null if input is null.
    /// </summary>
    internal static string? Sanitize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var needsSanitization = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c) || c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069'))
            {
                needsSanitization = true;
                break;
            }
        }

        if (!needsSanitization)
        {
            return value;
        }

        var buffer = new char[value.Length];
        var pos = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsControl(c) && c is not ((>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069')))
            {
                buffer[pos++] = c;
            }
        }

        return new string(buffer, 0, pos);
    }
}
