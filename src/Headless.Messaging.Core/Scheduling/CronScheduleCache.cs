// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Cronos;
using Microsoft.Extensions.Caching.Memory;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Thread-safe cache for parsed <see cref="CronExpression"/> instances.
/// Normalizes whitespace before caching so that expressions differing only
/// in spacing resolve to the same cached entry.
/// Uses <see cref="MemoryCache"/> with sliding expiration so that recurring
/// jobs keep entries warm while one-time expressions are naturally evicted.
/// </summary>
internal sealed partial class CronScheduleCache : IDisposable
{
    private static readonly TimeSpan _SlidingExpiration = TimeSpan.FromHours(1);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });

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

        var tz = string.IsNullOrWhiteSpace(timeZone) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timeZone);

        var next = expression.GetNextOccurrence(from.UtcDateTime, tz);

        return next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
    }

    /// <inheritdoc />
    public void Dispose() => _cache.Dispose();

    private CronExpression _GetOrParse(string cron)
    {
        var key = _Normalize(cron);

        return _cache.GetOrCreate(
            key,
            static entry =>
            {
                entry.SlidingExpiration = _SlidingExpiration;
                entry.Size = 1;
                return CronExpression.Parse((string)entry.Key, CronFormat.IncludeSeconds);
            }
        )!;
    }

    private static string _Normalize(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return _WhitespaceRegex().Replace(expression.Trim(), " ");
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _WhitespaceRegex();
}
