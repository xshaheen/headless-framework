using NCrontab;

namespace Framework.Ticker.Utilities;

using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

internal static partial class CronScheduleCache
{
    public static TimeZoneInfo TimeZoneInfo { get; internal set; } = TimeZoneInfo.Local;

    private static readonly ConcurrentDictionary<string, CrontabSchedule> _Cache = new(StringComparer.Ordinal);

    private static readonly CrontabSchedule.ParseOptions _Opts = new() { IncludingSeconds = true };

    private static string _Normalize(string expr)
    {
        ArgumentNullException.ThrowIfNull(expr);

        return _ReplaceRegex().Replace(expr.Trim(), " ");
    }

    public static CrontabSchedule? Get(string expression)
    {
        var key = _Normalize(expression);

        return _Cache.GetOrAdd(key, exp => CrontabSchedule.TryParse(exp, _Opts)!);
    }

    public static DateTime? GetNextOccurrenceOrDefault(string expression, DateTime dateTime)
    {
        var parsed = Get(_Normalize(expression));

        if (parsed == null)
        {
            return null;
        }

        var localTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, TimeZoneInfo);

        var nextOccurrence = parsed.GetNextOccurrence(localTime);

        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(nextOccurrence, TimeZoneInfo);

        return utcDateTime;
    }

    public static bool Invalidate(string expression) => _Cache.TryRemove(_Normalize(expression), out _);

    [GeneratedRegex(@"\s+")]
    private static partial Regex _ReplaceRegex();
}
