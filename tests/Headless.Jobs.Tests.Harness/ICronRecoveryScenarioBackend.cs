// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;

namespace Tests;

/// <summary>The coalesced run a recovery pass reported, reduced to what every backend can answer for.</summary>
/// <param name="Id">Identity of the run.</param>
/// <param name="ExecutionTimeUtc">The instant it stands at.</param>
/// <param name="RecoveredFromUtc">Its durable recovery stamp.</param>
/// <param name="OwnerId">Lease owner, which a repurposed run must no longer carry.</param>
public sealed record CronRecoveryRunSnapshot(
    Guid Id,
    DateTime ExecutionTimeUtc,
    DateTime? RecoveredFromUtc,
    string? OwnerId
);

/// <summary>What a recovery pass returned.</summary>
/// <param name="CoalescedRun">The single run standing in for the backlog, or <see langword="null" />.</param>
/// <param name="SkippedOccurrenceCount">How many still-claimable rows it retired.</param>
/// <param name="ReconciledThroughUtc">The watermark it reports having persisted.</param>
/// <param name="NextDueUtc">The projection it reports having persisted.</param>
public sealed record CronRecoveryOutcomeSnapshot(
    CronRecoveryRunSnapshot? CoalescedRun,
    int SkippedOccurrenceCount,
    DateTime ReconciledThroughUtc,
    DateTime NextDueUtc
);

/// <summary>
/// One persisted occurrence row, read back after recovery. <see cref="Status" /> and <see cref="Disposition" /> are
/// text rather than enums because a scenario deliberately seeds a status no binary in this repo writes — projecting
/// it as an enum would throw on the read instead of exercising the fail-closed rule.
/// </summary>
/// <param name="Id">Row identity.</param>
/// <param name="ExecutionTimeUtc">The instant the row stands at.</param>
/// <param name="Status">Persisted status text.</param>
/// <param name="Disposition">Persisted accounting disposition text.</param>
/// <param name="OwnerId">Persisted lease owner.</param>
/// <param name="RecoveredFromUtc">Persisted recovery stamp.</param>
public sealed record CronOccurrenceRowSnapshot(
    Guid Id,
    DateTime ExecutionTimeUtc,
    string Status,
    string Disposition,
    string? OwnerId,
    DateTime? RecoveredFromUtc
);

/// <summary>
/// One scenario's store: a single cron definition whose watermark sits before instant 0 of the grid, plus whatever
/// rows the scenario seeds. Created by <see cref="ICronRecoveryScenarioBackend.BeginScenarioAsync" />.
/// </summary>
public interface ICronRecoveryScenarioWorld
{
    /// <summary>The definition under recovery.</summary>
    Guid CronJobId { get; }

    /// <summary>The persisted watermark the scenario's request must observe.</summary>
    DateTime ObservedReconciledThroughUtc { get; }

    /// <summary>The persisted schedule revision the scenario's request must observe.</summary>
    long ScheduleRevision { get; }

    /// <summary>Instant 0 of the grid — the definition's first missed instant.</summary>
    DateTime FirstInstantUtc { get; }

    /// <summary>Seeds one occurrence and returns the id it was given.</summary>
    Task<Guid> SeedOccurrenceAsync(
        CronRecoverySeedRow row,
        DateTime executionTimeUtc,
        CancellationToken cancellationToken
    );

    /// <summary>Runs the real recovery entry point. <see langword="null" /> means the fence was lost.</summary>
    Task<CronRecoveryOutcomeSnapshot?> ApplyRecoveryAsync(
        CronRecoveryRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>Reads every occurrence row of this definition back out of the store.</summary>
    Task<IReadOnlyList<CronOccurrenceRowSnapshot>> ReadOccurrencesAsync(CancellationToken cancellationToken);

    /// <summary>Reads the definition's persisted schedule position back out of the store.</summary>
    Task<(DateTime ReconciledThroughUtc, DateTime NextDueUtc)> ReadSchedulePositionAsync(
        CancellationToken cancellationToken
    );
}

/// <summary>
/// A Jobs persistence backend the shared recovery scenarios can be driven against. Implemented once per provider —
/// in-memory directly over the provider, relational over a Testcontainers fixture — so a single scenario set proves
/// the same decision on all of them.
/// </summary>
public interface ICronRecoveryScenarioBackend : IAsyncDisposable
{
    /// <summary>Backend label, surfaced in every assertion so a failure names the provider.</summary>
    string BackendName { get; }

    /// <summary>Clears the store and brings up whatever host the scenarios need. Called once per test method.</summary>
    Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>Creates a fresh definition for one scenario.</summary>
    Task<ICronRecoveryScenarioWorld> BeginScenarioAsync(string scenarioName, CancellationToken cancellationToken);
}
