// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    internal static IEnumerable<string> ParsePathSegments(string path)
    {
        var segments = UrlEncoder
            .EncodeIllegalCharacters(path)
            .Replace("?", "%3F", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal)
            .Split('/');

        if (segments.Length == 0)
        {
            yield break;
        }

        // skip first and/or last segment if either empty, but not any in between. "///" should return 2 empty segments for example.
        var start = segments[0].Length > 0 ? 0 : 1;
        var count = segments.Length - (segments[^1].Length > 0 ? 0 : 1);

        for (var i = start; i < count; i++)
        {
            yield return segments[i];
        }
    }
}
