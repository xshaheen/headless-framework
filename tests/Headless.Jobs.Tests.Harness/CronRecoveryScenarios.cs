// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Tests;

/// <summary>
/// The one scenario set that drives the coalesce-recovery decision on every provider (#834).
/// </summary>
/// <remarks>
/// <para>
/// The decision — which instant to materialize at, which row to repurpose, which to step past, which to retire, and
/// where the resolution window ends under a saturated evaluation — lives in <c>CronRecoveryPlanner</c>. It used to be
/// hand-mirrored per provider, covered on the in-memory side by unit tests and on the relational side by the EF
/// harness, with CI running only the former. A divergence therefore surfaced as a comment mismatch rather than as a
/// failing test. This set removes the asymmetry: the identical scenarios run against every backend, so a change to
/// the decision fails everywhere at once and a change to one backend's mechanics fails there alone.
/// </para>
/// <para>
/// Each scenario seeds real rows and calls the real <c>ApplyCronRecoveryAsync</c> end to end. None of them hand-seeds
/// the plan the planner is supposed to produce.
/// </para>
/// </remarks>
public static class CronRecoveryScenarios
{
    private const string _OtherNode = "node-b@1";

    /// <summary>Every scenario, in reading order.</summary>
    public static CronRecoveryScenario[] All =>
        [
            new()
            {
                Name = "empty-window-materializes-the-owed-run",
                Contract = "a backlog with no rows at all owes exactly one run, at its earliest missed instant",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Created, 0),
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 1,
            },
            new()
            {
                Name = "skip-retires-the-backlog-and-owes-nothing",
                Contract = "under Skip no run stands in for the backlog, and every still-claimable row is retired",
                Policy = MissedRunPolicy.Skip,
                MissedInstantIndexes = [0, 1],
                BoundedProgressIndex = 1,
                RecoveredThroughIndex = 3,
                SeedRows =
                [
                    new("backlog", 0, JobStatus.Idle),
                    new("claimed", 1, JobStatus.Queued, OwnerId: _OtherNode),
                ],
                ExpectedRun = null,
                ExpectedSkippedCount = 2,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 2,
                ExpectedRows =
                [
                    new("backlog", JobStatus.Skipped, CronOccurrenceDisposition.Accounted),
                    new("claimed", JobStatus.Skipped, CronOccurrenceDisposition.Accounted),
                ],
            },
            new()
            {
                Name = "an-idle-row-is-repurposed-in-place",
                Contract = "a still-claimable row at the missed instant is revived rather than duplicated",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                SeedRows = [new("row", 0, JobStatus.Idle)],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Repurposed, 0, "row"),
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 1,
                ExpectedRows = [new("row", JobStatus.Idle, CronOccurrenceDisposition.Accounted)],
            },
            new()
            {
                Name = "a-queued-row-is-repurposed-and-its-owner-revoked",
                Contract =
                    "the claim path's in-progress transition requires OwnerId == owner, so clearing the owner is what "
                    + "makes the prior holder drop the row",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                SeedRows = [new("row", 0, JobStatus.Queued, OwnerId: _OtherNode)],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Repurposed, 0, "row"),
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 1,
                ExpectedRows = [new("row", JobStatus.Idle, CronOccurrenceDisposition.Accounted)],
            },
            new()
            {
                Name = "an-executing-row-is-left-alone-and-its-instant-is-not-duplicated",
                Contract = "a second run at an instant another node is executing would duplicate work in flight",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                SeedRows = [new("running", 0, JobStatus.InProgress, OwnerId: _OtherNode)],
                ExpectedRun = null,
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 1,
                ExpectedRows = [new("running", JobStatus.InProgress, CronOccurrenceDisposition.Accounted)],
            },
            new()
            {
                Name = "a-completed-instant-is-stepped-past-onto-an-empty-one",
                Contract =
                    "the later tick was genuinely missed even though the earliest one ran, so the backlog is still "
                    + "owed its single run — one instant later",
                MissedInstantIndexes = [0, 1],
                BoundedProgressIndex = 1,
                RecoveredThroughIndex = 3,
                SeedRows = [new("done", 0, JobStatus.Succeeded)],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Created, 1),
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 2,
                ExpectedRows = [new("done", JobStatus.Succeeded, CronOccurrenceDisposition.Accounted)],
            },
            new()
            {
                Name = "a-completed-instant-is-stepped-past-onto-a-repurposable-one",
                Contract = "stepping past an accounted instant must land on the next row, not create a duplicate",
                MissedInstantIndexes = [0, 1],
                BoundedProgressIndex = 1,
                RecoveredThroughIndex = 3,
                SeedRows = [new("done", 0, JobStatus.Succeeded), new("next", 1, JobStatus.Idle)],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Repurposed, 1, "next"),
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 2,
                ExpectedRows =
                [
                    new("done", JobStatus.Succeeded, CronOccurrenceDisposition.Accounted),
                    new("next", JobStatus.Idle, CronOccurrenceDisposition.Accounted),
                ],
            },
            new()
            {
                Name = "a-fully-accounted-backlog-owes-no-run-at-all",
                Contract = "only a backlog whose every missed instant is accounted for produces nothing",
                MissedInstantIndexes = [0, 1],
                BoundedProgressIndex = 1,
                RecoveredThroughIndex = 3,
                SeedRows = [new("first", 0, JobStatus.Succeeded), new("second", 1, JobStatus.Cancelled)],
                ExpectedRun = null,
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 2,
            },
            new()
            {
                Name = "a-migration-retired-row-still-owes-its-fire",
                Contract =
                    "KTD1: the seeding migration retires a row WITHOUT a replacement, so the instant is unaccounted "
                    + "for and owes a NEW run — resurrecting the retired row instead would undo the migration",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                SeedRows =
                [
                    new(
                        "retired",
                        0,
                        JobStatus.Skipped,
                        CronOccurrenceDisposition.ReplacementOwed,
                        SkippedReason: "Cron definition updated"
                    ),
                ],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Created, 0),
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 2,
                ExpectedRows = [new("retired", JobStatus.Skipped, CronOccurrenceDisposition.ReplacementOwed)],
            },
            new()
            {
                Name = "a-superseded-row-accounts-for-its-instant",
                Contract =
                    "KTD1a: the runtime edit path writes the IDENTICAL SkippedReason and installs its own "
                    + "replacement, so re-firing here would double-run every expression edit",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                SeedRows =
                [
                    new(
                        "superseded",
                        0,
                        JobStatus.Skipped,
                        CronOccurrenceDisposition.Superseded,
                        SkippedReason: "Cron definition updated"
                    ),
                ],
                ExpectedRun = null,
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 1,
                ExpectedRows = [new("superseded", JobStatus.Skipped, CronOccurrenceDisposition.Superseded)],
            },
            new()
            {
                Name = "an-unrecognized-status-fails-closed",
                Contract =
                    "a status written by a newer binary must neither throw on the read nor become a silent re-fire",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                SeedRows = [new("future", 0, JobStatus.Idle, RawStatus: "SomeFutureStatus")],
                ExpectedRun = null,
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 1,
            },
            new()
            {
                Name = "a-live-row-is-repurposed-over-an-older-terminal-one",
                Contract =
                    "R3a: the unique index constrains only live rows, so both can stand at one instant — ordering by "
                    + "CreatedAt alone would pick the terminal one and repurpose nothing",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                SeedRows =
                [
                    new("terminal", 0, JobStatus.Cancelled, CreatedAtRank: 0),
                    new("live", 0, JobStatus.Idle, CreatedAtRank: 1),
                ],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Repurposed, 0, "live"),
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 2,
                ExpectedRows =
                [
                    new("terminal", JobStatus.Cancelled, CronOccurrenceDisposition.Accounted),
                    new("live", JobStatus.Idle, CronOccurrenceDisposition.Accounted),
                ],
            },
            new()
            {
                Name = "residual-rows-are-retired-and-the-run-is-preserved",
                Contract =
                    "the single coalesced run stands in for the whole backlog, so every OTHER still-claimable row in "
                    + "the window is retired — and the run itself is never one of them",
                MissedInstantIndexes = [0],
                BoundedProgressIndex = 0,
                RecoveredThroughIndex = 3,
                SeedRows =
                [
                    new("run", 0, JobStatus.Idle),
                    new("residual", 1, JobStatus.Idle),
                    new("done", 2, JobStatus.Succeeded),
                ],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Repurposed, 0, "run"),
                ExpectedSkippedCount = 1,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 3,
                ExpectedRows =
                [
                    new("run", JobStatus.Idle, CronOccurrenceDisposition.Accounted),
                    new("residual", JobStatus.Skipped, CronOccurrenceDisposition.Accounted),
                    new("done", JobStatus.Succeeded, CronOccurrenceDisposition.Accounted),
                ],
            },
            new()
            {
                Name = "a-saturated-page-that-finds-no-run-keeps-its-unexamined-successor",
                Contract =
                    "an unexamined instant beyond a saturated page is the NEXT pass's only coalesce candidate: "
                    + "advancing the watermark past it, or retiring it, drops the run the backlog is still owed",
                EvaluationSaturated = true,
                MissedInstantIndexes = [0, 1],
                BoundedProgressIndex = 1,
                RecoveredThroughIndex = 3,
                SeedRows =
                [
                    new("first", 0, JobStatus.Succeeded),
                    new("second", 1, JobStatus.Succeeded),
                    new("unexamined", 2, JobStatus.Idle),
                ],
                ExpectedRun = null,
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 1,
                ExpectedNextDueIndex = 2,
                ExpectedRowCount = 3,
                ExpectedRows =
                [
                    new("first", JobStatus.Succeeded, CronOccurrenceDisposition.Accounted),
                    new("second", JobStatus.Succeeded, CronOccurrenceDisposition.Accounted),
                    new("unexamined", JobStatus.Idle, CronOccurrenceDisposition.Accounted),
                ],
            },
            new()
            {
                Name = "a-saturated-page-that-repurposes-its-run-resolves-the-whole-window",
                Contract =
                    "having found the one run the backlog owes, a saturated page stands for the full store-time "
                    + "window, so rows beyond its examined prefix are retired and the watermark carries all the way",
                EvaluationSaturated = true,
                MissedInstantIndexes = [0, 1],
                BoundedProgressIndex = 1,
                RecoveredThroughIndex = 3,
                SeedRows =
                [
                    new("first", 0, JobStatus.Succeeded),
                    new("candidate", 1, JobStatus.Idle),
                    new("beyond", 2, JobStatus.Idle),
                ],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Repurposed, 1, "candidate"),
                ExpectedSkippedCount = 1,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 3,
                ExpectedRows =
                [
                    new("first", JobStatus.Succeeded, CronOccurrenceDisposition.Accounted),
                    new("candidate", JobStatus.Idle, CronOccurrenceDisposition.Accounted),
                    new("beyond", JobStatus.Skipped, CronOccurrenceDisposition.Accounted),
                ],
            },
            new()
            {
                Name = "a-saturated-page-that-creates-its-run-resolves-the-whole-window",
                Contract =
                    "the same full-window resolution follows when the owed run is materialized rather than revived",
                EvaluationSaturated = true,
                MissedInstantIndexes = [0, 1],
                BoundedProgressIndex = 1,
                RecoveredThroughIndex = 3,
                SeedRows = [new("first", 0, JobStatus.Succeeded)],
                ExpectedRun = new CronRecoveryRunExpectation(CronRecoveryRunOrigin.Created, 1),
                ExpectedSkippedCount = 0,
                ExpectedReconciledThroughIndex = 3,
                ExpectedNextDueIndex = 4,
                ExpectedRowCount = 2,
                ExpectedRows = [new("first", JobStatus.Succeeded, CronOccurrenceDisposition.Accounted)],
            },
        ];
}
