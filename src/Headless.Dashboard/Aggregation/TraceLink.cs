// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>Builds the <c>Open trace</c> link from the host's trace link template and an object's W3C trace id.</summary>
internal static class TraceLink
{
    private const string _Placeholder = "{traceId}";

    /// <summary>
    /// Returns <paramref name="template"/> with <paramref name="traceId"/> in place of <c>{traceId}</c>, or
    /// <see langword="null"/> when the host set no template or the id is not a valid W3C trace id.
    /// </summary>
    /// <remarks>
    /// The id comes from a module's store, so only a W3C trace id (32 lower-case hex digits, not all zero) is put into
    /// the link: anything else could change the link's path or query.
    /// </remarks>
    public static string? Build(string? template, string? traceId)
    {
        if (string.IsNullOrEmpty(template) || !_IsW3CTraceId(traceId))
        {
            return null;
        }

        return template.Replace(_Placeholder, traceId, StringComparison.Ordinal);
    }

    private static bool _IsW3CTraceId([NotNullWhen(true)] string? traceId)
    {
        if (traceId is not { Length: 32 })
        {
            return false;
        }

        var allZero = true;

        foreach (var c in traceId)
        {
            if (!char.IsAsciiHexDigitLower(c))
            {
                return false;
            }

            allZero &= c == '0';
        }

        return !allZero;
    }
}
