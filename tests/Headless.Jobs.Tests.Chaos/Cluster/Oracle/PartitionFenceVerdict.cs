// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests.Cluster;

/// <summary>One read of a lease the partitioned node held: when it was read and the deadline the store held then.</summary>
public sealed record LeaseSample(TimeSpan At, Guid RowId, long Generation, DateTimeOffset? ExpiresAt);

/// <summary>
/// The fence half of the coordination-only partition oracle, from the documented contract: once the node passes
/// <c>DeadThreshold - SelfFenceMargin</c> after its last accepted heartbeat it starts nothing new, no renewal it makes
/// succeeds, and membership loss cancels the token of every run it holds at once. The last accepted heartbeat began no
/// later than the cut, so <c>cut + DeadThreshold - SelfFenceMargin</c> bounds the deadline from above.
/// </summary>
/// <param name="Runner">The partitioned incarnation.</param>
/// <param name="CutAt">When its coordination route was cut, on the ledger clock.</param>
/// <param name="DeadlineBound">The latest instant its self-fence deadline can fall on.</param>
/// <param name="FenceObservedAt">When the node's membership-loss token fired, or null if it never did.</param>
public sealed record PartitionFenceVerdict(
    string Runner,
    TimeSpan CutAt,
    TimeSpan DeadlineBound,
    TimeSpan? FenceObservedAt
)
{
    /// <summary>How late the fence timer may fire after its deadline on a loaded runner.</summary>
    public static readonly TimeSpan TimerSlack = TimeSpan.FromSeconds(2);

    /// <summary>The gap between the identity check before a body and the body's own start record.</summary>
    public static readonly TimeSpan StartSlack = TimeSpan.FromSeconds(1);

    /// <summary>How soon after the fence a run that honors its token must have ended.</summary>
    public static readonly TimeSpan Promptness = TimeSpan.FromSeconds(2);

    /// <summary>The fewest bodies the node must be running at the cut for the scenario to prove anything.</summary>
    public const int MinimumRunningAtCut = 2;

    public IReadOnlyList<string> Violations(
        IReadOnlyList<Attempt> attempts,
        IReadOnlyDictionary<Guid, JobSpec> specs,
        IReadOnlyList<LeaseSample> leases
    )
    {
        var violations = new List<string>();
        var mine = attempts.Where(attempt => string.Equals(attempt.Runner, Runner, StringComparison.Ordinal)).ToList();
        var runningAtCut = mine.Count(attempt =>
            attempt.StartedAt <= CutAt && (attempt.StoppedAt is null || attempt.StoppedAt > CutAt)
        );

        if (runningAtCut < MinimumRunningAtCut)
        {
            violations.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"precondition: {Runner} ran {runningAtCut} body(ies) at the cut, fewer than {MinimumRunningAtCut}"
                )
            );
        }

        if (FenceObservedAt is not { } fenced)
        {
            violations.Add($"{Runner} never lost its membership after its coordination route was cut");
            fenced = DeadlineBound;
        }
        else if (fenced > DeadlineBound + TimerSlack)
        {
            violations.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Runner} fenced at {fenced.TotalSeconds:F2}s, after its deadline bound {DeadlineBound.TotalSeconds:F2}s"
                )
            );
        }

        var lateStarts = mine.Where(attempt => attempt.StartedAt > DeadlineBound + StartSlack).ToList();

        if (lateStarts.Count > 0)
        {
            violations.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{lateStarts.Count} body(ies) started on {Runner} after its fence deadline: {string.Join(", ", lateStarts.Take(5).Select(a => string.Create(CultureInfo.InvariantCulture, $"{a.JobId} at {a.StartedAt.TotalSeconds:F2}s")))}"
                )
            );
        }

        var fenceAt = fenced < DeadlineBound ? fenced : DeadlineBound;
        var notCancelled = mine.Where(attempt =>
                specs.TryGetValue(attempt.JobId, out var spec)
                && !spec.IgnoresToken
                && attempt.StartedAt < fenceAt
                && (attempt.EndedAt is null || attempt.EndedAt > fenced + Promptness)
            )
            .ToList();

        if (notCancelled.Count > 0)
        {
            violations.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{notCancelled.Count} run(s) on {Runner} were not cancelled within {Promptness.TotalSeconds:F0}s of the fence: {string.Join(", ", notCancelled.Take(5).Select(_DescribeEnd))}"
                )
            );
        }

        // Renewals the fenced node made after its deadline: the first read after it may still show a renewal made just
        // before, so only a deadline that moves later between two reads past the deadline counts.
        var renewed = leases
            .Where(sample => sample.At > DeadlineBound + StartSlack && sample.ExpiresAt is not null)
            .GroupBy(sample => (sample.RowId, sample.Generation))
            .Where(group =>
            {
                var ordered = group.OrderBy(sample => sample.At).ToList();
                return ordered.Skip(1).Any(sample => sample.ExpiresAt > ordered[0].ExpiresAt);
            })
            .Select(group => group.Key.RowId)
            .ToList();

        if (renewed.Count > 0)
        {
            violations.Add(
                $"{renewed.Count} lease(s) held by {Runner} were renewed after its fence deadline: {string.Join(", ", renewed.Take(5))}"
            );
        }

        return violations;
    }

    private static string _DescribeEnd(Attempt attempt) =>
        $"{attempt.JobId} ended {attempt.End?.ToString() ?? "never"} at {attempt.EndedAt?.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) ?? "-"}";

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Runner} cut at {CutAt.TotalSeconds:F2}s, deadline bound {DeadlineBound.TotalSeconds:F2}s, fenced at {FenceObservedAt?.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) ?? "never"}"
        );
}
