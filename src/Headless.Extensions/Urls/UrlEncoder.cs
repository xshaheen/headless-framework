// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Headless.Urls;

internal static partial class UrlEncoder
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
            // Uri.EscapeDataString is going to throw because the string is "too long", so break it into pieces and concat them
            var parts = new string[(int)Math.Ceiling((double)s.Length / _MaxUrlLength)];
            for (var i = 0; i < parts.Length; i++)
            {
                var start = i * _MaxUrlLength;
                var len = Math.Min(_MaxUrlLength, s.Length - start);
                parts[i] = Uri.EscapeDataString(s.AsSpan(start, len));
            }
            s = string.Concat(parts);
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

        // no % characters, so avoid the regex overhead
        if (!s.OrdinalContains("%"))
        {
#pragma warning disable SYSLIB0013 // Type or member is obsolete
            return Uri.EscapeUriString(s);
        }
#pragma warning restore SYSLIB0013

        // pick out all %-hex-hex matches and avoid double-encoding
        return _EscapeRegex()
            .Replace(
                s,
                c =>
                {
                    var a = c.Groups[1].Value; // group 1 is a sequence with no %-encoding - encode illegal characters
                    var b = c.Groups[2].Value; // group 2 is a valid 3-character %-encoded sequence - leave it alone!
#pragma warning disable SYSLIB0013 // Type or member is obsolete
                    return Uri.EscapeUriString(a) + b;
#pragma warning restore SYSLIB0013
                }
            );
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

    [GeneratedRegex("(.*?)((%[0-9A-Fa-f]{2})|$)", RegexOptions.Compiled | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _EscapeRegex();
}
