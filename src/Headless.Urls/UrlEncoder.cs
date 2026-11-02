// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Urls;

internal static class UrlEncoder
{
    private const int _MaxUrlLength = 65519;

    /// <summary>
    /// URL-encodes a string, including reserved characters such as '/' and '?'.
    /// </summary>
    /// <param name="s">The string to encode.</param>
    /// <param name="encodeSpaceAsPlus">If true, spaces will be encoded as + signs. Otherwise, they'll be encoded as %20.</param>
    /// <returns>The encoded URL.</returns>
    [return: NotNullIfNotNull(nameof(s))]
    internal static string? Encode(string? s, bool encodeSpaceAsPlus = false)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (s.Length > _MaxUrlLength)
        {
            // Uri.EscapeDataString throws for very long strings, so escape it in chunks and concat. A chunk
            // boundary must never split a UTF-16 surrogate pair, or EscapeDataString corrupts the non-BMP
            // character straddling it, so pull the boundary back one char when it lands on a high surrogate.
            var sb = new StringBuilder(s.Length);
            var start = 0;
            while (start < s.Length)
            {
                var len = Math.Min(_MaxUrlLength, s.Length - start);
                if (start + len < s.Length && char.IsHighSurrogate(s[start + len - 1]))
                {
                    len--;
                }

                sb.Append(Uri.EscapeDataString(s.AsSpan(start, len)));
                start += len;
            }

            s = sb.ToString();
        }
        else
        {
            s = Uri.EscapeDataString(s);
        }

        return encodeSpaceAsPlus ? s.Replace("%20", "+", StringComparison.Ordinal) : s;
    }

    /// <summary>
    /// URL-encodes characters in a string that are neither reserved nor unreserved. Avoids encoding reserved characters such as '/' and '?'. Avoids encoding '%' if it begins a %-hex-hex sequence (i.e. avoids double-encoding).
    /// </summary>
    /// <param name="s">The string to encode.</param>
    /// <param name="encodeSpaceAsPlus">If true, spaces will be encoded as + signs. Otherwise, they'll be encoded as %20.</param>
    /// <returns>The encoded URL.</returns>
    [return: NotNullIfNotNull(nameof(s))]
    internal static string? EncodeIllegalCharacters(string? s, bool encodeSpaceAsPlus = false)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (encodeSpaceAsPlus)
        {
            s = s.Replace(' ', '+');
        }

        // Uri.EscapeUriString mostly does what we want - encodes illegal characters only - but it has a quirk
        // in that % isn't illegal if it's the start of a %-encoded sequence https://stackoverflow.com/a/47636037/62600

        // no % characters, so the whole string can be escaped in one call
        if (!s.OrdinalContains("%"))
        {
#pragma warning disable SYSLIB0013 // Type or member is obsolete
            return Uri.EscapeUriString(s);
        }
#pragma warning restore SYSLIB0013

        // Copy each valid %-hex-hex triplet verbatim and escape the runs between them, so an already-encoded
        // sequence is never double-encoded while a stray '%' still becomes %25.
        var sb = new StringBuilder(s.Length + 16);
        var runStart = 0;
        var i = s.IndexOf('%', StringComparison.Ordinal);

        while (i >= 0)
        {
            if (i + 2 < s.Length && char.IsAsciiHexDigit(s[i + 1]) && char.IsAsciiHexDigit(s[i + 2]))
            {
                _AppendEscapedRun(sb, s, runStart, i);
                sb.Append(s, i, 3);
                runStart = i + 3;
                i = s.IndexOf('%', runStart);
            }
            else
            {
                i = s.IndexOf('%', i + 1);
            }
        }

        _AppendEscapedRun(sb, s, runStart, s.Length);

        return sb.ToString();
    }

    private static void _AppendEscapedRun(StringBuilder sb, string s, int start, int end)
    {
        if (start >= end)
        {
            return;
        }

#pragma warning disable SYSLIB0013 // Type or member is obsolete
        sb.Append(Uri.EscapeUriString(s[start..end]));
#pragma warning restore SYSLIB0013
    }

    /// <summary>
    /// Decodes a URL-encoded string.
    /// </summary>
    /// <param name="s">The URL-encoded string.</param>
    /// <param name="interpretPlusAsSpace">If true, any '+' character will be decoded to a space.</param>
    [return: NotNullIfNotNull(nameof(s))]
    internal static string? Decode(string? s, bool interpretPlusAsSpace)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        return Uri.UnescapeDataString(interpretPlusAsSpace ? s.Replace('+', ' ') : s);
    }
}
