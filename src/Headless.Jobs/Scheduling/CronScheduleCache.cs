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

    private readonly ConcurrentDictionary<CadenceKey, bool> _cadenceVerdicts = new();

    private static readonly CrontabSchedule.ParseOptions _Opts = new() { IncludingSeconds = true };

    private static string _Normalize(string expr)
    {
        Argument.IsNotNull(expr);

        return ReplaceRegex.Replace(expr.Trim(), " ");
    }

    public CrontabSchedule Get(string expression)
    {
        Argument.IsNotNull(expression);

        // Key by the raw expression so normalization (Trim + regex replace) runs once per distinct expression
        // inside the factory, not on every evaluation — the scheduler's dispatch loop calls this per pending
        // instant at up to ~kHz cadence. Differently-spaced variants of one expression cache separately; they
        // parse to the same schedule, so only cache size is affected.
        return _cache.GetOrAdd(expression, static exp => CrontabSchedule.TryParse(_Normalize(exp), _Opts)!);
    }

    public DateTime? GetNextOccurrenceOrDefault(string expression, DateTime dateTime)
    {
        return GetNextOccurrenceOrDefault(expression, dateTime, timeZoneId: null);
    }

    /// <summary>
    /// The earliest instant strictly after <paramref name="dateTime"/> at which <paramref name="expression"/> fires in
    /// the zone <paramref name="timeZoneId"/> resolves to, or <see langword="null"/> when it never fires again.
    /// </summary>
    /// <remarks>
    /// The expression and the zone's rules define one set of UTC fire instants, and this returns its next member. The
    /// result is a pure function of the expression, the zone's rules, and the cursor, which is what lets every
    /// scheduling path walk a schedule one result at a time and agree on the instants.
    /// <list type="bullet">
    /// <item>
    /// A spring-forward gap: every occurrence whose local time does not exist fires shifted forward by the gap, and
    /// occurrences that land on the same instant fire once.
    /// </item>
    /// <item>
    /// A fall-back overlap: the first pass of a repeated local time always fires. The second pass fires only when the
    /// expression's cadence continues across the repeated window, meaning its occurrence offsets inside that window
    /// equal those in the window of the same length right before it or right after it. <c>0 */15 * * * *</c> fires on
    /// both passes; <c>0 30 1 * * *</c> fires once.
    /// </item>
    /// </list>
    /// The cursor stays in UTC throughout: one local time can name both passes of an overlap, so converting the cursor
    /// to local time would lose which pass it is in.
    /// </remarks>
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
        var cursor = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        var offset = CronZoneTransition.OffsetAt(timeZone, cursor);
        var candidate = _NextAtOffset(parsed, cursor, offset);

        // The hot path: no transition near the cursor or near the candidate, so the zone behaves like a fixed offset
        // between them. A transition further away cannot matter, because every local time it repeats or removes lies
        // strictly between the cursor's local time and the candidate's, where the expression has no occurrence.
        if (
            _OffsetUnchangedAround(timeZone, cursor, offset)
            && (candidate is not { } fast || _OffsetUnchangedAround(timeZone, fast, offset))
        )
        {
            return candidate;
        }

        return _NextAcrossTransitions(parsed, expression, timeZone, cursor);
    }

    private static bool _OffsetUnchangedAround(TimeZoneInfo timeZone, DateTime utc, TimeSpan offset)
    {
        var radius = CronZoneTransition.SearchRadius;

        return CronZoneTransition.OffsetAt(timeZone, CronZoneTransition.Add(utc, -radius)) == offset
            && CronZoneTransition.OffsetAt(timeZone, CronZoneTransition.Add(utc, radius)) == offset;
    }

    /// <summary>
    /// Evaluates one stretch of constant offset at a time, in UTC order, starting with the one holding the cursor.
    /// </summary>
    /// <remarks>
    /// Inside a stretch the members come from two ordered streams: the local enumeration mapped by the stretch's own
    /// offset, and, right after a gap, the local times the gap removed mapped by the offset before it. Each stream is
    /// monotone in UTC, so the earlier head is the next member even when a shifted occurrence lands after a later local
    /// one. A head at or past the next transition belongs to the next stretch, which is evaluated with its own offset,
    /// so a sparse schedule is mapped with the offset in effect on its own date.
    /// </remarks>
    private DateTime? _NextAcrossTransitions(
        CrontabSchedule schedule,
        string expression,
        TimeZoneInfo timeZone,
        DateTime cursor
    )
    {
        var lower = cursor;
        var stretch = cursor;

        while (true)
        {
            var offset = CronZoneTransition.OffsetAt(timeZone, stretch);
            var previous = CronZoneTransition.FindPrevious(timeZone, stretch);
            var from = lower;
            DateTime? candidate = null;

            if (previous is { IsGap: true } gap)
            {
                // A local time the gap removed keeps the offset before the gap, which moves it forward by the gap.
                if (
                    _NextAtOffset(schedule, lower, gap.OffsetBefore) is { } shifted
                    && CronZoneTransition.Add(shifted, gap.OffsetBefore) < gap.LocalAfter
                )
                {
                    candidate = shifted;
                }
            }
            else if (previous is { } overlap && !_ContinuesCadence(schedule, expression, timeZone, overlap))
            {
                // The second passes are the instants [T, T + D); a schedule whose cadence breaks there skips them.
                var secondPassEnd = overlap.InstantUtc + (overlap.OffsetBefore - overlap.OffsetAfter);

                if (from < secondPassEnd)
                {
                    from = secondPassEnd.AddTicks(-1);
                }
            }

            if (_NextAtOffset(schedule, from, offset) is { } mapped && (candidate is null || mapped < candidate))
            {
                candidate = mapped;
            }

            if (candidate is not { } next)
            {
                return null;
            }

            if (CronZoneTransition.FindNext(timeZone, stretch, next) is not { } transition)
            {
                return next;
            }

            stretch = transition.InstantUtc;
            lower = transition.InstantUtc.AddTicks(-1);
        }
    }

    /// <summary>
    /// The first occurrence strictly after <paramref name="lowerUtc"/> with every local time mapped by the single
    /// <paramref name="offset"/>, or <see langword="null"/> when the expression has none.
    /// </summary>
    private static DateTime? _NextAtOffset(CrontabSchedule schedule, DateTime lowerUtc, TimeSpan offset)
    {
        var local = DateTime.SpecifyKind(CronZoneTransition.Add(lowerUtc, offset), DateTimeKind.Unspecified);
        var next = schedule.GetNextOccurrence(local);

        if (_HasNoFutureOccurrence(next))
        {
            return null;
        }

        return DateTime.SpecifyKind(CronZoneTransition.Add(next, -offset), DateTimeKind.Utc);
    }

    // The verdict depends only on the expression and the transition, and a seconds-field expression enumerates
    // thousands of local occurrences to reach it, so it is computed once per transition rather than per call.
    private bool _ContinuesCadence(
        CrontabSchedule schedule,
        string expression,
        TimeZoneInfo timeZone,
        CronZoneTransition overlap
    )
    {
        return _cadenceVerdicts.GetOrAdd(
            new CadenceKey(expression, timeZone.Id, overlap),
            static (key, schedule) =>
            {
                // Measured on local wall-clock occurrences only: the neighbouring windows are never mapped to UTC, so
                // one that crosses another transition needs no special case.
                var repeatedStart = key.Transition.LocalAfter;
                var length = key.Transition.OffsetBefore - key.Transition.OffsetAfter;

                return _SameOffsets(schedule, repeatedStart, repeatedStart - length, length)
                    || _SameOffsets(schedule, repeatedStart, repeatedStart + length, length);
            },
            schedule
        );
    }

    // Whether the occurrences in [first, first + length) sit at the same offsets from their window's start as those in
    // [second, second + length), compared at the cron expression's one-second resolution.
    private static bool _SameOffsets(CrontabSchedule schedule, DateTime first, DateTime second, TimeSpan length)
    {
        using var firstOccurrences = schedule.GetNextOccurrences(first.AddTicks(-1), first + length).GetEnumerator();
        using var secondOccurrences = schedule.GetNextOccurrences(second.AddTicks(-1), second + length).GetEnumerator();

        while (true)
        {
            var hasFirst = firstOccurrences.MoveNext();

            if (hasFirst != secondOccurrences.MoveNext())
            {
                return false;
            }

            if (!hasFirst)
            {
                return true;
            }

            if (firstOccurrences.Current - first != secondOccurrences.Current - second)
            {
                return false;
            }
        }
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

        Argument.IsPositiveOrZero(graceSeconds);
        var grace = graceSeconds == 0 ? JobsRecoveryDefaults.MissedRunGraceSeconds : graceSeconds;

        DateTime? earliest = null;
        DateTime? latest = null;
        var count = 0;
        var cursor = reconciledThroughUtc;

        // Deferred to the SECOND pending instant. The ordinary tick — one due instant, no backlog — is by far the
        // hottest caller and never reads PendingInstantsUtc: it is consumed only by the coalesce walk on the recovery
        // path. Allocating a list per tick to hold a single element nobody looks at is pure waste, so the single-instant
        // case materializes one array at return instead, and only when the result is actually a recovery.
        List<DateTime>? instants = null;

        // The instant that ended the walk, whether by falling past storeUtcNow or by exhausting the expression. Both
        // the saturation probe and the caller's next projection want exactly this value, and all three used to compute
        // it separately.
        DateTime? nextAfterPending;

        while (true)
        {
            var next = GetNextOccurrenceOrDefault(expression, cursor, timeZoneId);

            if (next is null || next.Value > storeUtcNow)
            {
                nextAfterPending = next;

                break;
            }

            if (count == 1)
            {
                instants = [earliest!.Value];
            }

            earliest ??= next.Value;
            latest = next.Value;
            cursor = next.Value;
            instants?.Add(next.Value);
            count++;

            if (count == ceiling)
            {
                // Ceiling reached rather than exhausted: probe once past the last pending instant so saturation and the
                // caller's projection share the single evaluation.
                nextAfterPending = GetNextOccurrenceOrDefault(expression, cursor, timeZoneId);

                break;
            }
        }

        if (earliest is not { } earliestPending)
        {
            return CronPendingEvaluation.None;
        }

        // Saturated only when the walk stopped at the ceiling AND another pending instant exists past it, so a backlog
        // that lands exactly on the ceiling still reports an exact count.
        var saturated = count == ceiling && nextAfterPending is { } beyond && beyond <= storeUtcNow;

        var isRecovery = count > 1 || earliestPending < storeUtcNow.AddSeconds(-grace);

        return new CronPendingEvaluation
        {
            EarliestPendingUtc = earliest,
            LatestPendingUtc = latest,
            PendingCount = count,
            CountSaturated = saturated,
            // Only the recovery path reads this. A non-recovery single instant hands back the shared empty array rather
            // than allocating for a caller that will not look.
            PendingInstantsUtc = instants ?? (isRecovery ? [earliestPending] : []),
            IsRecovery = isRecovery,
            NextAfterPendingUtc = nextAfterPending,
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

    /// <summary>
    /// Whether THIS HOST can evaluate a definition in <paramref name="timeZoneId"/> at all. Every evaluation entry
    /// point resolves the zone first, so a <see langword="false"/> here means the whole definition is inert on this
    /// node — including <see cref="EvaluatePending"/> and every
    /// <see cref="GetNextOccurrenceOrDefault(string, DateTime, string?)"/> overload.
    /// </summary>
    /// <remarks>
    /// This is a NODE-LOCAL fact, not a verdict on the definition: it says the running host's timezone database has no
    /// entry for the identifier, which a peer with current tzdata will disagree with. Callers use it to skip work they
    /// cannot do here rather than to record anything durable about the definition (#830).
    /// </remarks>
    public bool CanResolveTimeZone(string? timeZoneId)
    {
        return CronTimeZoneResolver.TryResolve(timeZoneId, TimeZoneInfo, out _);
    }

    /// <summary>
    /// <see cref="ComputeEvaluationFingerprint"/> for a zone this host may not have: returns <see langword="false"/>
    /// instead of throwing when <paramref name="timeZoneId"/> does not resolve here.
    /// </summary>
    public bool TryComputeEvaluationFingerprint(string? timeZoneId, [NotNullWhen(true)] out string? fingerprint)
    {
        if (!CronTimeZoneResolver.TryResolve(timeZoneId, TimeZoneInfo, out var timeZone))
        {
            fingerprint = null;

            return false;
        }

        fingerprint = CronEvaluationFingerprint.Compute(timeZone);

        return true;
    }

    /// <summary>How many overlap cadence verdicts this cache holds, one per expression, zone, and transition.</summary>
    internal int CadenceVerdictCount => _cadenceVerdicts.Count;

    public bool Invalidate(string expression)
    {
        // Remove both the raw key (post-change cache layout) and the normalized key (pre-change entries), so
        // invalidation also clears anything cached before the keying change within a running process.
        var removed = _cache.TryRemove(expression, out _);
        return _cache.TryRemove(_Normalize(expression), out _) || removed;
    }

    private readonly record struct CadenceKey(string Expression, string TimeZoneId, CronZoneTransition Transition);

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReplaceRegex { get; }
}
