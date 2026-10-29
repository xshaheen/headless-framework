// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// HTTP syntax checks that options validators share, so a header name, a cookie name, and an origin are judged by one
/// rule wherever the framework accepts them.
/// </summary>
internal static class HttpSyntax
{
    /// <summary>
    /// RFC 9110 <c>token</c>: one or more <c>tchar</c>, where <c>tchar</c> is a letter, a digit, or one
    /// of <c>!#$%&amp;'*+-.^_`|~</c>. Header names and RFC 6265 cookie names share this grammar. A char loop rather
    /// than a regex: the alphabet is fixed and there is no pattern to time out.
    /// </summary>
    public static bool IsToken(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        const string tcharPunctuation = "!#$%&'*+-.^_`|~";

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && !tcharPunctuation.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns why <paramref name="origin"/> is not a bare serialized origin, or null when it is one.</summary>
    public static string? DescribeOriginProblem(string origin)
    {
        // Uri silently trims surrounding whitespace, which would hide a copy-paste error in configuration.
        if (origin.AsSpan().Trim().Length != origin.Length)
        {
            return "must not contain leading or trailing whitespace.";
        }

        // Any scheme with a host is a real origin: hybrid mobile webviews send 'capacitor://localhost' or
        // 'ionic://localhost'. A file URL serializes to the opaque origin 'null', so it can never match.
        if (
            !origin.Contains("://", StringComparison.Ordinal)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.Ordinal)
        )
        {
            return "must be an absolute origin with a scheme and host, such as 'https://app.example.com'.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "must not contain user info.";
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "must not contain a query or fragment.";
        }

        // Uri normalizes an empty path to '/', so read the raw string: 'https://a.com/' and 'https://a.com/x' both
        // fail, since the browser's Origin header never carries a path.
        var authorityStart = origin.IndexOf("://", StringComparison.Ordinal) + 3;

        if (origin.IndexOf('/', authorityStart) >= 0)
        {
            return "must not contain a path or trailing slash.";
        }

        // Browsers drop a default port from the Origin header, and ASP.NET compares configured origins as lowercased
        // strings without dropping it, so 'https://a.com:443' would never match. The bracket check keeps an IPv6
        // literal's colons from reading as a port.
        var authority = origin.AsSpan(authorityStart);

        if (uri.IsDefaultPort && authority.LastIndexOf(':') > authority.LastIndexOf(']'))
        {
            return $"must not spell out the default port; write it as '{uri.Scheme}://{uri.Authority}'.";
        }

        return null;
    }
}
