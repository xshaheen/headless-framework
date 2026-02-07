// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Cronos;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Thread-safe cache for parsed <see cref="CronExpression"/> instances.
/// Normalizes whitespace before caching so that expressions differing only
/// in spacing resolve to the same cached entry.
/// </summary>
internal sealed partial class CronScheduleCache
{
    private readonly ConcurrentDictionary<string, CronExpression> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the next occurrence after <paramref name="from"/> for the given
    /// 6-field (with seconds) cron expression evaluated in the specified time zone.
    /// </summary>
    /// <param name="cron">A 6-field cron expression (seconds granularity).</param>
    /// <param name="timeZone">
    /// An IANA or system time-zone identifier. When <c>null</c> or empty, UTC is used.
    /// </param>
    /// <param name="from">The reference point from which to calculate the next occurrence.</param>
    /// <returns>
    /// The next <see cref="DateTimeOffset"/> occurrence, or <c>null</c> when no future
    /// occurrence can be determined within the Cronos year range.
    /// </returns>
    public DateTimeOffset? GetNextOccurrence(string cron, string? timeZone, DateTimeOffset from)
    {
        var expression = _GetOrParse(cron);

        var tz = string.IsNullOrWhiteSpace(timeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(timeZone);

        var next = expression.GetNextOccurrence(from.UtcDateTime, tz);

        return next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
    }

    private CronExpression _GetOrParse(string cron)
    {
        var key = _Normalize(cron);
        return _cache.GetOrAdd(key, static k => CronExpression.Parse(k, CronFormat.IncludeSeconds));
    }

    private static string _Normalize(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return _WhitespaceRegex().Replace(expression.Trim(), " ");
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _WhitespaceRegex();
}
