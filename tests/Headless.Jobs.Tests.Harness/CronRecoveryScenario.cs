// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Tests;

/// <summary>
/// One seeded occurrence, positioned on the scenario's instant grid. Times, ids, and the parent definition are the
/// backend's to materialize — a scenario names an instant by index so the identical description drives an in-memory
/// dictionary and a relational table.
/// </summary>
/// <param name="Key">
/// Stable label used to refer to this row from the expectations and to name it in assertion messages.
/// </param>
/// <param name="InstantIndex">Which instant on the grid the row stands at.</param>
/// <param name="Status">Persisted status.</param>
/// <param name="Disposition">Persisted accounting disposition — the sole input to the occupied-instant rule.</param>
/// <param name="OwnerId">Lease owner, or <see langword="null" /> for an unowned row.</param>
/// <param name="CreatedAtRank">
/// Relative creation order among rows sharing an instant. Lower is older. Only the ordering matters, so a scenario
/// can pin an older terminal row against a newer live one without knowing either wall-clock time.
/// </param>
/// <param name="SkippedReason">Display text a retired row carries; never an accounting input.</param>
/// <param name="RawStatus">
/// Marks the row as carrying a status no binary in this repo recognizes, overriding <paramref name="Status" />. Each
/// backend materializes that in the only way it can — a verbatim string in a relational column, an out-of-range enum
/// value in memory — so no expectation may assert the text back.
/// </param>
public sealed record CronRecoverySeedRow(
    string Key,
    int InstantIndex,
    JobStatus Status,
    CronOccurrenceDisposition Disposition = CronOccurrenceDisposition.Accounted,
    string? OwnerId = null,
    int CreatedAtRank = 0,
    string? SkippedReason = null,
    string? RawStatus = null
);

/// <summary>How the coalesced run came to exist.</summary>
public enum CronRecoveryRunOrigin
{
    /// <summary>Nothing accounted for the instant, so the run was materialized under the reserved id.</summary>
    Created,

    /// <summary>A still-claimable row stood at the instant and was revived in place.</summary>
    Repurposed,
}

/// <summary>The run a scenario expects recovery to establish.</summary>
/// <param name="Origin">Whether the run is new or a revived row.</param>
/// <param name="InstantIndex">Which grid instant it stands at.</param>
/// <param name="RepurposedKey">
/// Seed row the run reuses; <see langword="null" /> when <paramref name="Origin" /> is
/// <see cref="CronRecoveryRunOrigin.Created" />.
/// </param>
public sealed record CronRecoveryRunExpectation(
    CronRecoveryRunOrigin Origin,
    int InstantIndex,
    string? RepurposedKey = null
);

/// <summary>The durable state a seeded row is expected to be left in.</summary>
/// <param name="Key">Which seed row.</param>
/// <param name="Status">Its status after recovery.</param>
/// <param name="Disposition">Its disposition after recovery.</param>
public sealed record CronRecoveryRowExpectation(string Key, JobStatus Status, CronOccurrenceDisposition Disposition);

/// <summary>
/// One end-to-end recovery scenario: a store state, a recovery request shape, and the durable outcome EVERY provider
/// owes for it.
/// </summary>
/// <remarks>
/// <para>
/// Instants live on a grid: instant <c>i</c> is the definition's first missed instant plus <c>i</c> hours, and the
/// observed watermark sits before instant 0. Everything else — row identities, the parent definition, the store's
/// clock — belongs to the backend.
/// </para>
/// <para>
/// Expectations are stated LITERALLY rather than derived from <c>CronRecoveryPlanner</c>. A table computed from the
/// production decision would agree with any decision, including a wrong one, and this set exists precisely to fail
/// when the decision changes.
/// </para>
/// </remarks>
public sealed record CronRecoveryScenario
{
    /// <summary>Case label, surfaced in every assertion so a failure names the scenario.</summary>
    public required string Name { get; init; }

    /// <summary>Why this scenario exists — quoted into failures so a red run explains itself.</summary>
    public required string Contract { get; init; }

    /// <summary>Which policy resolves the backlog.</summary>
    public MissedRunPolicy Policy { get; init; } = MissedRunPolicy.Coalesce;

    /// <summary>Whether more elapsed instants existed beyond the evaluation page.</summary>
    public bool EvaluationSaturated { get; init; }

    /// <summary>The missed instants the evaluation walk visited, as grid indexes in schedule order.</summary>
    public required int[] MissedInstantIndexes { get; init; }

    /// <summary>Grid index of the last instant a saturated evaluation examined.</summary>
    public required int BoundedProgressIndex { get; init; }

    /// <summary>Grid index of the recovery instant — the watermark an unsaturated pass carries to.</summary>
    public required int RecoveredThroughIndex { get; init; }

    /// <summary>Rows already in the store when recovery runs.</summary>
    public CronRecoverySeedRow[] SeedRows { get; init; } = [];

    /// <summary>The run recovery must establish, or <see langword="null" /> when it owes none.</summary>
    public CronRecoveryRunExpectation? ExpectedRun { get; init; }

    /// <summary>How many rows recovery must retire.</summary>
    public required int ExpectedSkippedCount { get; init; }

    /// <summary>Grid index of the watermark recovery must persist.</summary>
    public required int ExpectedReconciledThroughIndex { get; init; }

    /// <summary>Grid index of the projection recovery must persist.</summary>
    public required int ExpectedNextDueIndex { get; init; }

    /// <summary>Total occurrence rows for the definition after recovery.</summary>
    public required int ExpectedRowCount { get; init; }

    /// <summary>The durable state each seeded row must be left in.</summary>
    public CronRecoveryRowExpectation[] ExpectedRows { get; init; } = [];
}
