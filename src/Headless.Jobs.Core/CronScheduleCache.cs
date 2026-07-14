// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Headless.Checks;
using NCrontab;

namespace Headless.Jobs;

internal sealed partial class CronScheduleCache(TimeZoneInfo timeZoneInfo)
{
    public TimeZoneInfo TimeZoneInfo { get; } = Argument.IsNotNull(timeZoneInfo);

    private readonly ConcurrentDictionary<string, CrontabSchedule> _cache = new(StringComparer.Ordinal);

    private static readonly CrontabSchedule.ParseOptions _Opts = new() { IncludingSeconds = true };

    private static string _Normalize(string expr)
    {
        Argument.IsNotNull(expr);

        return ReplaceRegex.Replace(expr.Trim(), " ");
    }

    public CrontabSchedule Get(string expression)
    {
        var key = _Normalize(expression);

        return _cache.GetOrAdd(key, static exp => CrontabSchedule.TryParse(exp, _Opts)!);
    }

    public DateTime? GetNextOccurrenceOrDefault(string expression, DateTime dateTime)
    {
        // Get(...) already normalizes its argument, so passing the raw expression normalizes once instead of
        // twice (the regex replace + Trim ran an extra time on the already-normalized string).
        var parsed = Get(expression);

        if (parsed == null)
        {
            return null;
        }

        var localTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, TimeZoneInfo);

        var nextOccurrence = parsed.GetNextOccurrence(localTime);
        var nextUtc = _ConvertScheduledLocalTimeToUtc(nextOccurrence);

        if (TimeZoneInfo.IsAmbiguousTime(localTime))
        {
            var offsets = TimeZoneInfo.GetAmbiguousTimeOffsets(localTime);
            var overlap = offsets.Max() - offsets.Min();
            var overlapOccurrence = parsed.GetNextOccurrence(localTime.Subtract(overlap));
            var overlapUtc = _ConvertScheduledLocalTimeToUtc(overlapOccurrence);

            if (overlapUtc > dateTime && overlapUtc < nextUtc)
            {
                return overlapUtc;
            }
        }

        return nextUtc;
    }

    private DateTime _ConvertScheduledLocalTimeToUtc(DateTime localTime)
    {
        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);

        if (TimeZoneInfo.IsInvalidTime(localTime))
        {
            // Preserve the requested wall-clock minute by shifting it through the spring-forward gap. For example,
            // 02:30 in a one-hour gap becomes 03:30 rather than collapsing every skipped occurrence to 03:00.
            var offsetBefore = TimeZoneInfo.GetUtcOffset(localTime.AddDays(-1));
            var offsetAfter = TimeZoneInfo.GetUtcOffset(localTime.AddDays(1));
            var gap = offsetAfter - offsetBefore;
            localTime = localTime.Add(gap > TimeSpan.Zero ? gap : TimeSpan.FromHours(1));
        }

        if (TimeZoneInfo.IsAmbiguousTime(localTime))
        {
            // Choose the later UTC instant (normally the standard-time offset) so one wall-clock occurrence runs
            // once, after the overlap, instead of being dispatched twice.
            var offset = TimeZoneInfo.GetAmbiguousTimeOffsets(localTime).Min();
            return new DateTimeOffset(localTime, offset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(localTime, TimeZoneInfo);
    }

    public bool Invalidate(string expression) => _cache.TryRemove(_Normalize(expression), out _);

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReplaceRegex { get; }
}
