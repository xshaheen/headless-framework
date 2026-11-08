// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Headless.Logging.Internal;

/// <summary>Rewrites the span tags that can carry a request path, for every declared credential path.</summary>
internal static class LogPathTags
{
    // url.path is what ASP.NET Core and the OpenTelemetry instrumentation set today; http.target (older semantic
    // conventions) carries the path and query, and url.full (client spans, some server instrumentation) the whole URL.
    private static readonly string[] _Tags = ["url.path", "http.target", "url.full"];

    public static void Redact(Activity activity, ILogPathRedactor[] redactors)
    {
        foreach (var tag in _Tags)
        {
            if (activity.GetTagItem(tag) is string value && TryRedact(value, redactors, out var redacted))
            {
                activity.SetTag(tag, redacted);
            }
        }
    }

    /// <summary>Replaces <paramref name="raw"/> with <paramref name="redacted"/> wherever a path tag contains it.</summary>
    public static void Replace(Activity activity, string raw, string redacted)
    {
        foreach (var tag in _Tags)
        {
            if (activity.GetTagItem(tag) is string value && value.Contains(raw, StringComparison.Ordinal))
            {
                activity.SetTag(tag, value.Replace(raw, redacted, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    /// Redacts the path part of a tag value: a path, a path and query, or an absolute URL. The query and fragment of a
    /// matching value are dropped, because a credential URL can carry the same secret there.
    /// </summary>
    public static bool TryRedact(string value, ILogPathRedactor[] redactors, [NotNullWhen(true)] out string? redacted)
    {
        redacted = null;
        var pathStart = 0;

        if (!value.StartsWith('/'))
        {
            var scheme = value.IndexOf("://", StringComparison.Ordinal);
            pathStart = scheme < 0 ? -1 : value.IndexOf('/', scheme + 3);

            if (pathStart < 0)
            {
                return false;
            }
        }

        var queryStart = value.AsSpan(pathStart).IndexOfAny('?', '#');
        var path = queryStart < 0 ? value[pathStart..] : value.Substring(pathStart, queryStart);

        if (!ILogPathRedactor.TryRedactAny(redactors, path, out var redactedPath))
        {
            return false;
        }

        redacted = string.Concat(value.AsSpan(0, pathStart), redactedPath);

        return true;
    }
}
