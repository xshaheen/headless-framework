// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests.Scheduling;

/// <summary>
/// The oracle of the cron race: every instant the schedule owes inside the window fires exactly once across the whole
/// cluster, whichever node runs it.
/// </summary>
/// <remarks>
/// <para>
/// Which instants are owed follows <c>docs/llms/jobs.md</c>. "Where the watermark starts": a runtime definition is
/// positioned at the store's instant when it is inserted, so only instants strictly after that anchor are owed, and the
/// scenario passes the anchor it read back as the window start. "Due times follow the store clock": dispatch decides
/// due-ness on the store clock, so the node clocks' skew moves no instant. The window ends at a store instant the
/// scenario read after the soak and then waited out until every occurrence up to it settled, so no instant at the end is
/// owed-but-not-yet-due.
/// </para>
/// <para>
/// "Misfire recovery": when more than one instant is pending at a wake (a schedule shorter than the wake latency does
/// this legitimately), <c>Coalesce</c>, the default, runs once for the whole backlog and reports the first missed instant
/// as its <c>ScheduledFor</c>. The later instants of that backlog are accounted for without a run of their own, so a
/// missing instant is excused only when the nearest earlier run is such a recovery run and a later regular run shows the
/// schedule resumed. "Overlap policy": the scenario
/// keeps the default <c>Allow</c>, so an unfinished earlier run never turns an instant into a skip.
/// </para>
/// </remarks>
public sealed record CronInstantVerdict(
    int Owed,
    int Fires,
    IReadOnlyList<DateTime> Duplicated,
    IReadOnlyList<DateTime> Missing,
    IReadOnlyList<DateTime> Excused,
    IReadOnlyList<DateTime> OffSchedule,
    int RecoveryRuns
)
{
    private const int _SampleSize = 10;

    /// <summary>Judges the recorded runs of one definition against the instants its window owes.</summary>
    /// <param name="owed">Every instant the schedule places inside the window, in order.</param>
    /// <param name="isOnSchedule">Whether the schedule places an instant at all, inside the window or not.</param>
    /// <param name="fires">Every run of the definition the scenario recorded, from every node.</param>
    public static CronInstantVerdict Evaluate(
        IReadOnlyList<DateTime> owed,
        Func<DateTime, bool> isOnSchedule,
        IReadOnlyList<CronFire> fires
    )
    {
        var byInstant = fires
            .GroupBy(fire => fire.ScheduledFor)
            .ToDictionary(group => group.Key, group => group.ToList());
        var ordered = fires.OrderBy(fire => fire.ScheduledFor).ToList();
        List<DateTime> missing = [];
        List<DateTime> excused = [];

        foreach (var instant in owed)
        {
            if (byInstant.ContainsKey(instant))
            {
                continue;
            }

            var previous = ordered.LastOrDefault(fire => fire.ScheduledFor < instant);

            // A coalesced backlog ends when the schedule fires on time again; an instant after a recovery run that no
            // later regular run follows is not backlog but a schedule that stopped firing, so it stays missing.
            var resumedAfter = ordered.Exists(fire => fire.ScheduledFor > instant && !fire.IsRecoveryRun);

            if (previous is { IsRecoveryRun: true } && resumedAfter)
            {
                excused.Add(instant);
            }
            else
            {
                missing.Add(instant);
            }
        }

        return new CronInstantVerdict(
            owed.Count,
            fires.Count,
            [.. byInstant.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key).Order()],
            missing,
            excused,
            [.. byInstant.Keys.Where(instant => !isOnSchedule(instant)).Order()],
            fires.Count(fire => fire.IsRecoveryRun)
        );
    }

    public IReadOnlyList<string> Violations()
    {
        List<string> violations = [];

        if (Owed == 0)
        {
            violations.Add("the window owed no instant, so the scenario exercised nothing");
        }

        if (Duplicated.Count > 0)
        {
            violations.Add($"{Duplicated.Count} instant(s) fired more than once: {_Sample(Duplicated)}");
        }

        if (Missing.Count > 0)
        {
            violations.Add($"{Missing.Count} owed instant(s) never fired: {_Sample(Missing)}");
        }

        if (OffSchedule.Count > 0)
        {
            violations.Add(
                $"{OffSchedule.Count} run(s) at an instant the schedule does not place: {_Sample(OffSchedule)}"
            );
        }

        return violations;
    }

    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"owed {Owed}, fires {Fires}, recovery runs {RecoveryRuns}, coalesced {Excused.Count}, duplicated {Duplicated.Count}, missing {Missing.Count}, off-schedule {OffSchedule.Count}"
        );

    /// <summary>The runs at the instants that broke the oracle, one per line, for the failure evidence.</summary>
    public string Evidence(IReadOnlyList<CronFire> fires)
    {
        var bad = Duplicated.Concat(Missing).Concat(OffSchedule).ToHashSet();
        var lines = fires
            .Where(fire => bad.Contains(fire.ScheduledFor))
            .OrderBy(fire => fire.ScheduledFor)
            .Select(fire => $"{fire.ScheduledFor:O} node={fire.Node} recovery={fire.IsRecoveryRun}")
            .Concat(Missing.Select(instant => $"{instant:O} MISSING"));

        return string.Join('\n', lines);
    }

    private static string _Sample(IReadOnlyList<DateTime> instants) =>
        string.Join(", ", instants.Take(_SampleSize).Select(x => x.ToString("O", CultureInfo.InvariantCulture)))
        + (instants.Count > _SampleSize ? ", ..." : string.Empty);
}

/// <summary>
/// The oracle of the DST soak: each definition's runs equal its golden fire list, instant for instant. The golden lists
/// are derived by hand from the zones' offsets and the rules in <c>docs/llms/jobs.md</c> "Cron across DST transitions",
/// never from the code under test.
/// </summary>
public static class FireListOracle
{
    /// <summary>Lists every way the recorded runs differ from the golden lists.</summary>
    /// <param name="label">The transition the lists cover, prefixed to every violation.</param>
    /// <param name="expected">Each function's golden list inside the window.</param>
    /// <param name="fires">Every run the scenario recorded.</param>
    /// <param name="allowRecoveryRuns">
    /// False when the scenario steps the clock onto every instant exactly, so no instant can be late and a recovery run
    /// can only mean the scheduler treated an instant as missed.
    /// </param>
    public static IReadOnlyList<string> Compare(
        string label,
        IReadOnlyDictionary<string, IReadOnlyList<DateTime>> expected,
        IReadOnlyList<CronFire> fires,
        bool allowRecoveryRuns = false
    )
    {
        List<string> violations = [];

        foreach (var (function, golden) in expected.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var actual = fires.Where(fire => string.Equals(fire.Function, function, StringComparison.Ordinal)).ToList();
            var counts = actual
                .GroupBy(fire => fire.ScheduledFor)
                .ToDictionary(group => group.Key, group => group.Count());
            var missing = golden.Where(instant => !counts.ContainsKey(instant)).ToList();
            var unexpected = counts.Keys.Where(instant => !golden.Contains(instant)).Order().ToList();
            var duplicated = counts.Where(pair => pair.Value > 1).Select(pair => pair.Key).Order().ToList();
            var recovery = actual.Where(fire => fire.IsRecoveryRun).Select(fire => fire.ScheduledFor).Order().ToList();

            _Add(violations, label, function, "never fired", missing);
            _Add(violations, label, function, "fired off the golden list", unexpected);
            _Add(violations, label, function, "fired more than once", duplicated);

            if (!allowRecoveryRuns)
            {
                _Add(violations, label, function, "ran as a missed-run recovery", recovery);
            }
        }

        var strays = fires
            .Where(fire => !expected.ContainsKey(fire.Function))
            .Select(fire => fire.Function)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (strays.Count > 0)
        {
            violations.Add($"{label}: run(s) of function(s) with no golden list: {string.Join(", ", strays)}");
        }

        return violations;
    }

    private static void _Add(
        List<string> violations,
        string label,
        string function,
        string what,
        List<DateTime> instants
    )
    {
        if (instants.Count == 0)
        {
            return;
        }

        violations.Add(
            $"{label}: {function} {what} at {string.Join(", ", instants.Select(x => x.ToString("O", CultureInfo.InvariantCulture)))}"
        );
    }
}
