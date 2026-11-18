// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Fencing;
using Headless.Jobs;

namespace Tests.Cluster;

/// <summary>A time-job row as the store holds it after the run.</summary>
public sealed record StoreRow(Guid Id, JobStatus Status, RowLeaseSnapshot Lease, int RetryCount)
{
    public bool IsTerminal => Status >= JobStatus.Succeeded;
}

/// <summary>
/// The oracle of a cluster run, judged on the side-effect ledger and the rows the store holds afterwards, against the
/// documented Jobs contract:
/// <list type="bullet">
/// <item><c>Skip</c> and <c>MarkFailed</c> jobs start at most once.</item>
/// <item>A <c>Retry</c> job starts at least once and at most <c>Retries + 1</c> times, plus one per graceful stop that
/// interrupted it, because a graceful stop hands the row back without charging the budget.</item>
/// <item>No loss: every enqueued job's row is terminal once the faults stopped and the recovery window passed.</item>
/// <item>No orphan outlives the bound: a row's active lease never names a non-live owner for longer than
/// <c>LeaseDuration + 2 x reconcile interval + sampling period</c>. In this suite every holder the liveness snapshot
/// does not list as Alive or Suspected counts, a foreign cluster's included.</item>
/// <item>No stale settle: a body that kept running after its run lost the row (terminal, re-leased, released, or past
/// its deadline on the store clock) must not have its completion land on that row.</item>
/// </list>
/// </summary>
public sealed record ClusterVerdict(IReadOnlyList<string> Violations, IReadOnlyList<Guid> ViolatingJobs, string Counts)
{
    private const int _SampleSize = 8;

    public static ClusterVerdict Evaluate(
        LedgerSnapshot ledger,
        IReadOnlyList<StoreRow> rows,
        IReadOnlyList<OrphanInterval> orphans,
        TimeSpan orphanBound
    )
    {
        var violations = new List<string>();
        var violating = new HashSet<Guid>();
        var byJob = ledger.Attempts.ToLookup(attempt => attempt.JobId);
        var rowsById = rows.ToDictionary(row => row.Id);

        var overStarted = new List<string>();
        var underStarted = new List<string>();

        foreach (var spec in ledger.Specs.Values.OrderBy(spec => spec.Index))
        {
            var attempts = byJob[spec.Id].ToList();
            var starts = attempts.Count;

            if (spec.Policy is NodeDeathPolicy.Skip or NodeDeathPolicy.MarkFailed)
            {
                if (starts > 1)
                {
                    overStarted.Add(_Describe(spec, starts, "at most 1"));
                    violating.Add(spec.Id);
                }

                continue;
            }

            var graceful = attempts.Count(attempt => attempt.End == AttemptEnd.GracefulStop);
            var limit = spec.Retries + 1 + graceful;

            if (starts > limit)
            {
                overStarted.Add(_Describe(spec, starts, $"at most {limit.ToString(CultureInfo.InvariantCulture)}"));
                violating.Add(spec.Id);
            }

            // A row the store holds as cancelled never owed a run; every other Retry job must have started once.
            if (starts == 0 && !(rowsById.TryGetValue(spec.Id, out var row) && row.Status == JobStatus.Cancelled))
            {
                underStarted.Add(_Describe(spec, starts, "at least 1"));
                violating.Add(spec.Id);
            }
        }

        _AddIf(violations, overStarted, "job(s) started more often than their policy allows");
        _AddIf(violations, underStarted, "Retry job(s) never started");

        var missing = ledger.Specs.Keys.Where(id => !rowsById.ContainsKey(id)).ToList();
        var stuck = ledger.Specs.Keys.Where(id => rowsById.TryGetValue(id, out var row) && !row.IsTerminal).ToList();
        _AddIf(violations, missing.ConvertAll(id => id.ToString()), "enqueued job(s) have no row");
        _AddIf(
            violations,
            stuck.ConvertAll(id => $"{id} {rowsById[id].Status} holder {rowsById[id].Lease.Holder ?? "-"}"),
            "job(s) are not terminal after the recovery window"
        );
        violating.UnionWith(missing);
        violating.UnionWith(stuck);

        var longOrphans = orphans.Where(interval => interval.Duration > orphanBound).ToList();
        _AddIf(
            violations,
            longOrphans.ConvertAll(interval =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"orphan on row {interval.Key.RowId} held by {interval.Key.Holder} for {interval.Duration.TotalSeconds:F1}s"
                )
            ),
            string.Create(
                CultureInfo.InvariantCulture,
                $"row(s) stayed owned by a non-live owner longer than {orphanBound.TotalSeconds:F1}s"
            )
        );
        violating.UnionWith(longOrphans.Select(interval => interval.Key.RowId));

        var staleSettles = ledger
            .Attempts.Where(attempt =>
                attempt.Observation is { } observation
                && observation.LostBy(attempt.Owner)
                && rowsById.TryGetValue(attempt.JobId, out var row)
                && _SettledAfterLoss(attempt, observation, row)
            )
            .ToList();
        _AddIf(
            violations,
            staleSettles.ConvertAll(attempt =>
                $"{attempt.JobId} completion by {attempt.Owner} landed after the run lost the row ({attempt.Observation})"
            ),
            "completion(s) landed on a row their run had lost"
        );
        violating.UnionWith(staleSettles.Select(attempt => attempt.JobId));

        return new ClusterVerdict(violations, [.. violating], _Counts(ledger, rows, orphans));
    }

    /// <summary>The ledger lines and final rows of the violating jobs, for the failure report.</summary>
    public string Evidence(LedgerSnapshot ledger, IReadOnlyList<StoreRow> rows, int maxJobs = 20)
    {
        if (ViolatingJobs.Count == 0)
        {
            return string.Empty;
        }

        var rowsById = rows.ToDictionary(row => row.Id);
        var builder = new StringBuilder();

        foreach (var id in ViolatingJobs.Take(maxJobs))
        {
            var spec = ledger.Specs.GetValueOrDefault(id);
            var described = spec is null
                ? " (not enqueued by the workload)"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $" #{spec.Index} {spec.Policy} retries {spec.Retries} work {spec.Work.TotalSeconds:F1}s{(spec.IgnoresToken ? " ignores-token" : string.Empty)}"
                );
            builder.AppendLine(CultureInfo.InvariantCulture, $"job {id}{described}");

            foreach (var attempt in ledger.Attempts.Where(attempt => attempt.JobId == id).OrderBy(a => a.StartedAt))
            {
                var ended = attempt.EndedAt?.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var killed = attempt.KilledAt is { } killedAt
                    ? string.Create(CultureInfo.InvariantCulture, $" (killed at {killedAt.TotalSeconds:F2}s)")
                    : string.Empty;
                var observed = attempt.Observation is { } observation
                    ? $"; before return saw {observation}"
                    : string.Empty;
                builder.AppendLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"  start {attempt.StartedAt.TotalSeconds:F2}s on {attempt.Runner} owner {attempt.Owner} retry {attempt.RetryCount}; end {attempt.End?.ToString() ?? "none"} at {ended}{killed}{observed}"
                    )
                );
            }

            builder.AppendLine(
                rowsById.TryGetValue(id, out var row)
                    ? $"  row {row.Status} lease {row.Lease.State} gen {row.Lease.Generation.ToString(CultureInfo.InvariantCulture)} holder {row.Lease.Holder ?? "-"} retry-count {row.RetryCount.ToString(CultureInfo.InvariantCulture)}"
                    : "  row missing"
            );
        }

        return builder.ToString();
    }

    // A late completion is attributable to this attempt when the row still carries the generation and holder the
    // attempt saw it lose, or when a sweep had already ended it terminally and the row now reads Succeeded instead.
    private static bool _SettledAfterLoss(Attempt attempt, RowObservation observation, StoreRow row)
    {
        // A time job that ran to its end settles as DueDone; Succeeded is its cron-occurrence twin.
        if (row.Status is not (JobStatus.Succeeded or JobStatus.DueDone))
        {
            return false;
        }

        var sameLease =
            row.Lease.Generation == observation.Generation
            && string.Equals(row.Lease.Holder, attempt.Owner, StringComparison.Ordinal);
        var overwroteTerminal = observation.Status is JobStatus.Failed or JobStatus.Skipped or JobStatus.Cancelled;

        return sameLease || overwroteTerminal;
    }

    private static string _Counts(
        LedgerSnapshot ledger,
        IReadOnlyList<StoreRow> rows,
        IReadOnlyList<OrphanInterval> orphans
    )
    {
        var attempts = ledger.Attempts;
        var byStatus = rows.GroupBy(row => row.Status)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key} {group.Count().ToString(CultureInfo.InvariantCulture)}");
        var ends = attempts
            .GroupBy(attempt => attempt.End?.ToString() ?? (attempt.KilledAt is null ? "Running" : "Killed"))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} {group.Count().ToString(CultureInfo.InvariantCulture)}");
        var lateObservations = attempts.Count(attempt => attempt.Observation?.LostBy(attempt.Owner) == true);
        var longestOrphan = orphans.Count == 0 ? TimeSpan.Zero : orphans.Max(interval => interval.Duration);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"enqueued {ledger.Specs.Count}, starts {attempts.Count}, multi-start jobs {attempts.GroupBy(a => a.JobId).Count(g => g.Skip(1).Any())}, "
                + $"concurrent-execution jobs {ConcurrentJobs(attempts)}; ends [{string.Join(", ", ends)}]; rows [{string.Join(", ", byStatus)}]; "
                + $"late bodies that saw their row lost {lateObservations}; orphan intervals {orphans.Count}, longest {longestOrphan.TotalSeconds:F1}s"
        );
    }

    /// <summary>
    /// How many jobs had two attempts running at once on the ledger clock. A killed attempt counts until its kill, and
    /// one still running at the end until forever. Reported, never judged: a Retry body that ignores its token can
    /// legitimately overlap the run that reclaimed it.
    /// </summary>
    public static int ConcurrentJobs(IReadOnlyList<Attempt> attempts) =>
        attempts
            .GroupBy(attempt => attempt.JobId)
            .Count(group =>
            {
                var ordered = group.OrderBy(attempt => attempt.StartedAt).ToList();
                var runningUntil = TimeSpan.MinValue;

                foreach (var attempt in ordered)
                {
                    if (attempt.StartedAt < runningUntil)
                    {
                        return true;
                    }

                    var stopped = attempt.StoppedAt ?? TimeSpan.MaxValue;
                    runningUntil = stopped > runningUntil ? stopped : runningUntil;
                }

                return false;
            });

    private static string _Describe(JobSpec spec, int starts, string allowed) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{spec.Id} ({spec.Policy}, retries {spec.Retries}) started {starts}x, {allowed}"
        );

    private static void _AddIf(List<string> violations, IReadOnlyList<string> items, string what)
    {
        if (items.Count == 0)
        {
            return;
        }

        violations.Add(
            string.Create(CultureInfo.InvariantCulture, $"{items.Count} {what}: ")
                + string.Join("; ", items.Take(_SampleSize))
                + (items.Count > _SampleSize ? "; ..." : string.Empty)
        );
    }
}
