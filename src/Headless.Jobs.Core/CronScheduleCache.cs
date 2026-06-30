// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Headless.Checks;
using NCrontab;

namespace Headless.Jobs;

internal static partial class CronScheduleCache
{
    public static TimeZoneInfo TimeZoneInfo { get; internal set; } = TimeZoneInfo.Local;

    private static readonly ConcurrentDictionary<string, CrontabSchedule> _Cache = new(StringComparer.Ordinal);

    private static readonly CrontabSchedule.ParseOptions _Opts = new() { IncludingSeconds = true };

    private static string _Normalize(string expr)
    {
        Argument.IsNotNull(expr);

        return ReplaceRegex.Replace(expr.Trim(), " ");
    }

    public static CrontabSchedule Get(string expression)
    {
        var key = _Normalize(expression);

        return _Cache.GetOrAdd(key, static exp => CrontabSchedule.TryParse(exp, _Opts)!);
    }

    public static DateTime? GetNextOccurrenceOrDefault(string expression, DateTime dateTime)
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

        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(nextOccurrence, TimeZoneInfo);

        return utcDateTime;
    }

    public static bool Invalidate(string expression) => _Cache.TryRemove(_Normalize(expression), out _);

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReplaceRegex { get; }
}
