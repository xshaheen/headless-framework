// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Fencing;
using Headless.Jobs;

namespace Tests.Claims;

/// <summary>A handler start, stamped with the scenario's process-wide event sequence.</summary>
public sealed record ClaimStart(Guid JobId, string Node, long Sequence);

/// <summary>A manager edit: the sequence taken just before the call, and whether the store accepted it.</summary>
public sealed record ClaimEdit(Guid JobId, long Sequence, bool Succeeded);

/// <summary>One read of a row's lease while the race ran.</summary>
public sealed record ClaimObservation(Guid JobId, long Generation, RowLeaseState State, string? Holder);

/// <summary>A row as it stood once the race was over.</summary>
public sealed record ClaimFinalRow(
    Guid JobId,
    JobStatus Status,
    RowLeaseState LeaseState,
    int RetryCount,
    bool HasExecutedAt,
    int Attempts
);

/// <summary>
/// The oracle of the claim race with mutation. It follows <c>docs/llms/jobs.md</c>: the lease model ("each claim gives
/// the row its own next generation"; a completion leaves the lease <c>Settled</c>) and the manager contract
/// ("<c>ITimeJobManager.UpdateAsync</c> edits only a job no node has started: one that is <c>Idle</c> and unowned, or
/// <c>Queued</c>"; "run-state columns (status, owner, lease, retry count, outcome) are never taken from the caller's
/// entity").
/// </summary>
/// <remarks>
/// The sequence is one counter the whole process shares, taken by a handler when it starts and by the mutator just before
/// it calls <c>UpdateAsync</c>. A handler starts only after its row is <c>InProgress</c>, so an edit whose call began
/// after a start of the same row met a started row, and the edit guard had to refuse it. The edits the mutator sends
/// carry the entity it read, run-state columns included, so a guard that wrote them would rewind a claimed row: that
/// shows as a second start, a lease that is not <c>Settled</c>, a lost <c>ExecutedAt</c>, or a retry count.
/// </remarks>
public sealed record ClaimHistoryVerdict(
    int Rows,
    int Starts,
    int Edits,
    int EditsAccepted,
    int Observations,
    int ClaimedObservations,
    IReadOnlyList<string> RunTwice,
    IReadOnlyList<string> NeverRan,
    IReadOnlyList<string> EditAfterStart,
    IReadOnlyList<string> SharedGeneration,
    IReadOnlyList<string> BadFinalState
)
{
    private const int _SampleSize = 10;

    /// <summary>Judges one race from everything it recorded.</summary>
    public static ClaimHistoryVerdict Evaluate(
        IReadOnlyCollection<Guid> rows,
        IReadOnlyList<ClaimStart> starts,
        IReadOnlyList<ClaimEdit> edits,
        IReadOnlyList<ClaimObservation> observations,
        IReadOnlyList<ClaimFinalRow> finals
    )
    {
        var startsByJob = starts
            .GroupBy(x => x.JobId)
            .ToDictionary(x => x.Key, x => x.OrderBy(s => s.Sequence).ToList());
        var finalsByJob = finals.ToDictionary(x => x.JobId);
        List<string> runTwice = [];
        List<string> neverRan = [];
        List<string> badFinal = [];

        foreach (var job in rows.Order())
        {
            if (!startsByJob.TryGetValue(job, out var jobStarts))
            {
                neverRan.Add(_Id(job));
            }
            else if (jobStarts.Count > 1)
            {
                runTwice.Add(
                    $"{_Id(job)} started {jobStarts.Count}x on [{string.Join(", ", jobStarts.Select(s => s.Node))}]"
                );
            }

            if (!finalsByJob.TryGetValue(job, out var final))
            {
                badFinal.Add($"{_Id(job)} is gone");

                continue;
            }

            var problems = new List<string>();

            if (final.Status is not (JobStatus.Succeeded or JobStatus.DueDone))
            {
                problems.Add($"status {final.Status}");
            }

            if (final.LeaseState != RowLeaseState.Settled)
            {
                problems.Add($"lease {final.LeaseState}");
            }

            if (final.RetryCount != 0)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"retry count {final.RetryCount}"));
            }

            if (!final.HasExecutedAt && final.Status is JobStatus.Succeeded or JobStatus.DueDone)
            {
                problems.Add("no ExecutedAt");
            }

            if (final.Attempts != 1)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{final.Attempts} attempts"));
            }

            if (problems.Count > 0)
            {
                badFinal.Add($"{_Id(job)}: {string.Join(", ", problems)}");
            }
        }

        var editAfterStart = edits
            .Where(edit =>
                edit.Succeeded
                && startsByJob.TryGetValue(edit.JobId, out var jobStarts)
                && jobStarts[0].Sequence < edit.Sequence
            )
            .Select(edit =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_Id(edit.JobId)} edit #{edit.Sequence} accepted after start #{startsByJob[edit.JobId][0].Sequence}"
                )
            )
            .ToList();

        // Within one generation the lease has one holder; a second holder means two claims drew the same generation.
        var sharedGeneration = observations
            .Where(x => x.State == RowLeaseState.Active && x.Holder is not null)
            .GroupBy(x => (x.JobId, x.Generation))
            .Where(x => x.Select(o => o.Holder).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(x =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_Id(x.Key.JobId)} generation {x.Key.Generation} held by [{string.Join(", ", x.Select(o => o.Holder).Distinct(StringComparer.Ordinal))}]"
                )
            )
            .ToList();

        return new ClaimHistoryVerdict(
            rows.Count,
            starts.Count,
            edits.Count,
            edits.Count(x => x.Succeeded),
            observations.Count,
            observations.Count(x => x.State == RowLeaseState.Active),
            runTwice,
            neverRan,
            editAfterStart,
            sharedGeneration,
            badFinal
        );
    }

    public IReadOnlyList<string> Violations()
    {
        List<string> violations = [];
        _Add(violations, "row(s) ran more than once", RunTwice);
        _Add(violations, "row(s) never ran", NeverRan);
        _Add(violations, "edit(s) the guard accepted on a started row", EditAfterStart);
        _Add(violations, "generation(s) held by two owners", SharedGeneration);
        _Add(violations, "row(s) ended with run-state the run did not write", BadFinalState);

        if (Rows == 0)
        {
            violations.Add("the race seeded no row, so it exercised nothing");
        }

        return violations;
    }

    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"rows {Rows}, starts {Starts}, edits {Edits} ({EditsAccepted} accepted), lease reads {Observations} ({ClaimedObservations} claimed), run twice {RunTwice.Count}, never ran {NeverRan.Count}, edit after start {EditAfterStart.Count}, shared generation {SharedGeneration.Count}, bad final {BadFinalState.Count}"
        );

    public string Evidence() =>
        string.Join(
            '\n',
            RunTwice.Concat(NeverRan).Concat(EditAfterStart).Concat(SharedGeneration).Concat(BadFinalState)
        );

    private static void _Add(List<string> violations, string what, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        violations.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{items.Count} {what}: {string.Join("; ", items.Take(_SampleSize))}{(items.Count > _SampleSize ? "; ..." : string.Empty)}"
            )
        );
    }

    private static string _Id(Guid id) => id.ToString("N", CultureInfo.InvariantCulture)[..8];
}
