// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Tests;

/// <summary>
/// Drives one <see cref="CronRecoveryScenario" /> against one backend, end to end, and asserts the durable outcome.
/// </summary>
/// <remarks>
/// Nothing here reads the plan the planner produced. The scenario seeds rows, calls the real
/// <c>ApplyCronRecoveryAsync</c>, and then asserts only what a caller could observe: the run that came back, the
/// counts, and what the store actually holds afterwards. A decision changed inside the planner therefore has to
/// travel all the way through the provider's fenced writes to be seen — which is the point.
/// </remarks>
public static class CronRecoveryScenarioRunner
{
    /// <summary>
    /// PostgreSQL materializes <c>DateTime</c> at microsecond granularity while SQL Server keeps ticks, so an exact
    /// comparison would pass on one backend and fail on the other for a reason unrelated to the decision.
    /// </summary>
    private static readonly TimeSpan _InstantTolerance = TimeSpan.FromMicroseconds(1);

    /// <summary>Seeds the scenario, applies recovery, and asserts every observable outcome it names.</summary>
    public static async Task RunAsync(
        ICronRecoveryScenarioBackend backend,
        CronRecoveryScenario scenario,
        CancellationToken cancellationToken
    )
    {
        var world = await backend.BeginScenarioAsync(scenario.Name, cancellationToken);
        var because = $"[{backend.BackendName}] scenario '{scenario.Name}': {scenario.Contract}";

        // Seeded oldest-first so a scenario can pin an older terminal row against a newer live one at one instant.
        var seededIds = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var row in scenario.SeedRows.OrderBy(x => x.CreatedAtRank))
        {
            seededIds[row.Key] = await world.SeedOccurrenceAsync(
                row,
                _Instant(world, row.InstantIndex),
                cancellationToken
            );
        }

        var reservedRunId = Guid.NewGuid();
        var result = await world.ApplyRecoveryAsync(_Request(world, scenario, reservedRunId), cancellationToken);

        result.Should().NotBeNull($"{because} — the scenario holds the observed fence, so recovery must apply");
        var rows = await world.ReadOccurrencesAsync(cancellationToken);
        var position = await world.ReadSchedulePositionAsync(cancellationToken);

        _AssertRun(world, scenario, result!, rows, seededIds, reservedRunId, because);

        result!.SkippedOccurrenceCount.Should().Be(scenario.ExpectedSkippedCount, $"{because} — retirement count");

        _AssertPosition(world, scenario, result, position, because);

        rows.Should().HaveCount(scenario.ExpectedRowCount, $"{because} — occurrence rows after recovery");

        foreach (var expected in scenario.ExpectedRows)
        {
            var stored = rows.Single(x => x.Id == seededIds[expected.Key]);
            stored.Status.Should().Be(expected.Status.ToString(), $"{because} — seeded row '{expected.Key}' status");
            stored
                .Disposition.Should()
                .Be(expected.Disposition.ToString(), $"{because} — seeded row '{expected.Key}' disposition");
        }
    }

    private static void _AssertRun(
        ICronRecoveryScenarioWorld world,
        CronRecoveryScenario scenario,
        CronRecoveryOutcomeSnapshot result,
        IReadOnlyList<CronOccurrenceRowSnapshot> rows,
        Dictionary<string, Guid> seededIds,
        Guid reservedRunId,
        string because
    )
    {
        if (scenario.ExpectedRun is not { } expectedRun)
        {
            result.CoalescedRun.Should().BeNull($"{because} — no run is owed");

            return;
        }

        var runInstant = _Instant(world, expectedRun.InstantIndex);
        result.CoalescedRun.Should().NotBeNull($"{because} — a run is owed");

        var expectedRunId = expectedRun.Origin switch
        {
            CronRecoveryRunOrigin.Created => reservedRunId,
            CronRecoveryRunOrigin.Repurposed => seededIds[expectedRun.RepurposedKey!],
            _ => throw new InvalidOperationException($"Unhandled run origin in scenario '{scenario.Name}'."),
        };

        result
            .CoalescedRun!.Id.Should()
            .Be(
                expectedRunId,
                expectedRun.Origin is CronRecoveryRunOrigin.Created
                    ? $"{because} — the run is NEW, under the reserved identity"
                    : $"{because} — the run reuses the existing row rather than duplicating it"
            );
        result
            .CoalescedRun.ExecutionTimeUtc.Should()
            .BeCloseTo(runInstant, _InstantTolerance, $"{because} — the run stands at its instant");
        result
            .CoalescedRun.RecoveredFromUtc.Should()
            .NotBeNull($"{because} — the recovery stamp is durable, the watermark has moved past the backlog");
        result
            .CoalescedRun.RecoveredFromUtc!.Value.Should()
            .BeCloseTo(runInstant, _InstantTolerance, $"{because} — the stamp names the instant the run stands for");
        result.CoalescedRun.OwnerId.Should().BeNull($"{because} — a recovered run carries no lease");

        // The returned run could be right while the write behind it is wrong, so the durable row is checked too.
        var stored = rows.Single(x => x.Id == expectedRunId);
        stored.Status.Should().Be(JobStatus.Idle.ToString(), $"{because} — the persisted run remains claimable");
        stored.OwnerId.Should().BeNull($"{because} — the persisted run's ownership is revoked");
        stored
            .ExecutionTimeUtc.Should()
            .BeCloseTo(runInstant, _InstantTolerance, $"{because} — the persisted run's instant");
        stored
            .RecoveredFromUtc.Should()
            .NotBeNull($"{because} — the persisted run carries the stamp the claim paths project");
    }

    private static void _AssertPosition(
        ICronRecoveryScenarioWorld world,
        CronRecoveryScenario scenario,
        CronRecoveryOutcomeSnapshot result,
        (DateTime ReconciledThroughUtc, DateTime NextDueUtc) position,
        string because
    )
    {
        var expectedWatermark = _Instant(world, scenario.ExpectedReconciledThroughIndex);
        var expectedProjection = _Instant(world, scenario.ExpectedNextDueIndex);

        result
            .ReconciledThroughUtc.Should()
            .BeCloseTo(expectedWatermark, _InstantTolerance, $"{because} — reported watermark");
        result.NextDueUtc.Should().BeCloseTo(expectedProjection, _InstantTolerance, $"{because} — reported projection");
        position
            .ReconciledThroughUtc.Should()
            .BeCloseTo(expectedWatermark, _InstantTolerance, $"{because} — PERSISTED watermark");
        position
            .NextDueUtc.Should()
            .BeCloseTo(expectedProjection, _InstantTolerance, $"{because} — PERSISTED projection");
    }

    private static CronRecoveryRequest _Request(
        ICronRecoveryScenarioWorld world,
        CronRecoveryScenario scenario,
        Guid reservedRunId
    )
    {
        return new CronRecoveryRequest
        {
            CronJobId = world.CronJobId,
            ObservedReconciledThroughUtc = world.ObservedReconciledThroughUtc,
            ExpectedScheduleRevision = world.ScheduleRevision,
            RecoveredThroughUtc = _Instant(world, scenario.RecoveredThroughIndex),
            NextDueUtc = _Instant(world, scenario.RecoveredThroughIndex + 1),
            BoundedProgressThroughUtc = _Instant(world, scenario.BoundedProgressIndex),
            NextDueAfterBoundedProgressUtc = _Instant(world, scenario.BoundedProgressIndex + 1),
            EvaluationSaturated = scenario.EvaluationSaturated,
            Policy = scenario.Policy,
            EarliestMissedUtc = _Instant(world, scenario.MissedInstantIndexes[0]),
            MissedInstantsUtc = Array.ConvertAll(scenario.MissedInstantIndexes, index => _Instant(world, index)),
            CoalescedOccurrenceId = reservedRunId,
            OnNodeDeath = NodeDeathPolicy.Retry,
            OperationTimeUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>The scenario grid: instant <c>i</c> is the definition's first missed instant plus <c>i</c> hours.</summary>
    private static DateTime _Instant(ICronRecoveryScenarioWorld world, int index)
    {
        return world.FirstInstantUtc.AddHours(index);
    }
}
