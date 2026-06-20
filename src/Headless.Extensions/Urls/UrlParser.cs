// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Checks;

namespace Headless.Urls;

internal static class UrlParser
{
    /// <summary>
    /// Parses a URL query to a QueryParamCollection.
    /// </summary>
    /// <param name="query">The URL query to parse.</param>
    internal static QueryParamCollection ParseQueryParams(string? query) => new(query);

    /// <summary>
    /// Splits the given path into segments, encoding illegal characters, "?", and "#".
    /// </summary>
    /// <param name="path">The path to split.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    internal static IEnumerable<string> ParsePathSegments(string path)
    {
        Argument.IsNotNull(path);

        // Encode illegal characters once, then split on '/'. The '?'/'#' percent-encoding is deferred to the yielded
        // segments only (via _EncodePathSegment): it never produces or consumes a '/', so applying it per segment is
        // byte-for-byte identical to the previous whole-string Replace("?"...).Replace("#"...) while dropping those two
        // intermediate full-string allocations. The split/skip math is preserved verbatim.
        var segments = UrlEncoder.EncodeIllegalCharacters(path)!.Split('/');

        if (segments.Length == 0)
        {
            yield break;
        }

        // skip first and/or last segment if either empty, but not any in between. "///" should return 2 empty segments for example.
        var start = segments[0].Length > 0 ? 0 : 1;
        var count = segments.Length - (segments[^1].Length > 0 ? 0 : 1);

        for (var i = start; i < count; i++)
        {
            yield return _EncodePathSegment(segments[i]);
        }
    }

    /// <summary>
    /// Percent-encodes "?" and "#" in a single already-illegal-character-encoded path segment, allocating only when
    /// one of those characters is present.
    /// </summary>
    private static string _EncodePathSegment(string segment)
    {
        if (!segment.Contains('?', StringComparison.Ordinal) && !segment.Contains('#', StringComparison.Ordinal))
        {
            return segment;
        }

        var sb = new StringBuilder(segment.Length + 4);

        foreach (var c in segment)
        {
            switch (c)
            {
                case '?':
                    sb.Append("%3F");
                    break;
                case '#':
                    sb.Append("%23");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}
