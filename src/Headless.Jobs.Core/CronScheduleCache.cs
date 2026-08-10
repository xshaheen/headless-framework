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
        return GetNextOccurrenceOrDefault(expression, dateTime, timeZoneId: null);
    }

    public DateTime? GetNextOccurrenceOrDefault(string expression, DateTime dateTime, string? timeZoneId)
    {
        // Get(...) already normalizes its argument, so passing the raw expression normalizes once instead of
        // twice (the regex replace + Trim ran an extra time on the already-normalized string).
        var parsed = Get(expression);

        if (parsed == null)
        {
            return null;
        }

        var timeZone = CronTimeZoneResolver.Resolve(timeZoneId, TimeZoneInfo);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, timeZone);

        var nextOccurrence = parsed.GetNextOccurrence(localTime);
        if (_HasNoFutureOccurrence(nextOccurrence))
        {
            return null;
        }

        var nextUtc = _ConvertScheduledLocalTimeToUtc(nextOccurrence, timeZone);

        if (timeZone.IsAmbiguousTime(localTime))
        {
            var offsets = timeZone.GetAmbiguousTimeOffsets(localTime);
            var overlap = offsets.Max() - offsets.Min();
            var overlapOccurrence = parsed.GetNextOccurrence(localTime.Subtract(overlap));
            var overlapUtc = _ConvertScheduledLocalTimeToUtc(overlapOccurrence, timeZone);

            if (overlapUtc > dateTime && overlapUtc < nextUtc)
            {
                return overlapUtc;
            }
        }

        return nextUtc;
    }

    // NCrontab reports "no future occurrence" (e.g. `0 0 0 30 2 *`, Feb 30) by returning DateTime.MaxValue-era
    // values, NOT null or an error — without this guard such expressions were accepted as valid recurring jobs
    // and a permanently pending year-9999 occurrence row was materialized and re-leased forever. No legitimate
    // schedule computes a next occurrence in year 9999, and the sentinel is not timezone-stable, so detect it
    // before conversion.
    private static bool _HasNoFutureOccurrence(DateTime nextOccurrence)
    {
        return nextOccurrence.Year >= 9999;
    }

    private static DateTime _ConvertScheduledLocalTimeToUtc(DateTime localTime, TimeZoneInfo timeZone)
    {
        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(localTime))
        {
            // Preserve the requested wall-clock minute by shifting it through the spring-forward gap. For example,
            // 02:30 in a one-hour gap becomes 03:30 rather than collapsing every skipped occurrence to 03:00.
            var offsetBefore = timeZone.GetUtcOffset(localTime.AddDays(-1));
            var offsetAfter = timeZone.GetUtcOffset(localTime.AddDays(1));
            var gap = offsetAfter - offsetBefore;
            localTime = localTime.Add(gap > TimeSpan.Zero ? gap : TimeSpan.FromHours(1));
        }

        if (timeZone.IsAmbiguousTime(localTime))
        {
            // Choose the later UTC instant (normally the standard-time offset) so one wall-clock occurrence runs
            // once, after the overlap, instead of being dispatched twice.
            var offset = timeZone.GetAmbiguousTimeOffsets(localTime).Min();
            return new DateTimeOffset(localTime, offset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(localTime, timeZone);
    }

    /// <summary>
    /// Walks the schedule forward from <paramref name="reconciledThroughUtc"/> to decide what it owes as of
    /// <paramref name="storeUtcNow"/>, and whether that backlog is a misfire.
    /// </summary>
    /// <remarks>
    /// Evaluation goes through <see cref="GetNextOccurrenceOrDefault(string, DateTime, string?)"/> so the DST gap and
    /// overlap rules are applied exactly once, here as on the dispatch path. Re-deriving them would let a definition's
    /// backlog disagree with its own projection across a transition.
    /// <para>
    /// A paused span needs no special case: pause suspends selection and resume rebases the watermark to the resume
    /// instant, so the paused interval is never between the watermark and now and cannot produce a pending instant.
    /// </para>
    /// <para>
    /// The recovery decision is settled by the second pending instant at the latest, so the walk continues past that
    /// point purely to report a count. Both instants it returns stay exact regardless of saturation, because the walk
    /// visits them in order.
    /// </para>
    /// </remarks>
    public CronPendingEvaluation EvaluatePending(
        string expression,
        string? timeZoneId,
        DateTime reconciledThroughUtc,
        DateTime storeUtcNow,
        int graceSeconds,
        int evaluationCeiling = JobsRecoveryDefaults.EvaluationCeiling
    )
    {
        var ceiling = evaluationCeiling > 0 ? evaluationCeiling : JobsRecoveryDefaults.EvaluationCeiling;

        ArgumentOutOfRangeException.ThrowIfNegative(graceSeconds);
        var grace = graceSeconds == 0 ? JobsRecoveryDefaults.MissedRunGraceSeconds : graceSeconds;

        DateTime? earliest = null;
        DateTime? latest = null;
        var count = 0;
        var cursor = reconciledThroughUtc;
        var instants = new List<DateTime>();

        while (count < ceiling)
        {
            var next = GetNextOccurrenceOrDefault(expression, cursor, timeZoneId);

            if (next is null || next.Value > storeUtcNow)
            {
                break;
            }

            earliest ??= next.Value;
            latest = next.Value;
            cursor = next.Value;
            instants.Add(next.Value);
            count++;
        }

        if (count == 0)
        {
            return CronPendingEvaluation.None;
        }

        // Saturated only when the walk stopped at the ceiling AND another pending instant exists past it, so a backlog
        // that lands exactly on the ceiling still reports an exact count.
        var saturated =
            count == ceiling
            && GetNextOccurrenceOrDefault(expression, cursor, timeZoneId) is { } beyond
            && beyond <= storeUtcNow;

        var isRecovery = count > 1 || earliest!.Value < storeUtcNow.AddSeconds(-grace);

        return new CronPendingEvaluation
        {
            EarliestPendingUtc = earliest,
            LatestPendingUtc = latest,
            PendingCount = count,
            CountSaturated = saturated,
            PendingInstantsUtc = instants,
            IsRecovery = isRecovery,
        };
    }

    /// <summary>
    /// The fingerprint of the rules this cache would currently evaluate <paramref name="timeZoneId"/> under. A
    /// definition whose persisted fingerprint differs was positioned under rules that have since changed.
    /// </summary>
    /// <remarks>
    /// Resolved through the same <c>CronTimeZoneResolver</c> the evaluation path uses, so the fingerprint describes the
    /// zone actually applied — including the scheduler-wide fallback when the definition names none — rather than the
    /// identifier stored on the row.
    /// </remarks>
    public string ComputeEvaluationFingerprint(string? timeZoneId)
    {
        return CronEvaluationFingerprint.Compute(CronTimeZoneResolver.Resolve(timeZoneId, TimeZoneInfo));
    }

    public bool Invalidate(string expression)
    {
        return _cache.TryRemove(_Normalize(expression), out _);
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReplaceRegex { get; }
}
