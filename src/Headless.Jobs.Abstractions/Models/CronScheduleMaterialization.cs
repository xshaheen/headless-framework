// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>
/// Requests one atomic transition from an expected cron schedule position to a durable occurrence at the reconciled
/// instant.
/// </summary>
/// <remarks>
/// <see cref="ExecutionTimeUtc"/> must equal <see cref="CronScheduleAdvance.ReconciledThroughUtc"/>. The provider
/// arbitrates that occurrence key and the schedule-position fence in one transaction or critical section. Claiming
/// and leasing the resulting occurrence are deliberately separate operations.
/// </remarks>
[PublicAPI]
public sealed record CronScheduleMaterialization
{
    /// <summary>The fenced schedule-position transition to commit with the occurrence outcome.</summary>
    public required CronScheduleAdvance Advance { get; init; }

    /// <summary>The exact scheduled UTC instant whose durable occurrence is being accounted for.</summary>
    public required DateTime ExecutionTimeUtc { get; init; }
}

/// <summary>Outcome of an atomic cron schedule-position and occurrence transition.</summary>
/// <remarks>
/// New members may be added in future versions; consumers that <see langword="switch"/> on this enum should include
/// a default arm.
/// </remarks>
[PublicAPI]
public enum CronScheduleMaterializationOutcome
{
    /// <summary>The expected watermark, revision, or active-definition fence no longer held; nothing changed.</summary>
    LostFence = 0,

    /// <summary>The expected position held, but the store did not yet consider its projection due; nothing changed.</summary>
    NotDue = 1,

    /// <summary>A new unclaimed <see cref="JobStatus.Idle"/> occurrence and the new position committed together.</summary>
    OccurrenceCreated = 2,

    /// <summary>An existing non-terminal occurrence accounted for the instant and the new position committed.</summary>
    OccurrenceExists = 3,

    /// <summary>An existing terminal occurrence already accounted for the instant and the new position committed.</summary>
    OccurrenceAlreadyTerminal = 4,
}

/// <summary>Explicit result of an atomic cron schedule-position and occurrence transition.</summary>
[PublicAPI]
public sealed record CronScheduleMaterializationResult
{
    /// <summary>The durable outcome selected by the provider.</summary>
    public required CronScheduleMaterializationOutcome Outcome { get; init; }

    /// <summary>The committed position, or <see langword="null"/> when the fence was lost or the projection was not due.</summary>
    public CronScheduleAdvanceResult? SchedulePosition { get; init; }

    /// <summary>The occurrence that accounts for the instant, when one exists.</summary>
    public Guid? OccurrenceId { get; init; }

    /// <summary>The occurrence's durable creation timestamp, when one exists.</summary>
    public DateTimeOffset? OccurrenceCreatedAt { get; init; }

    /// <summary>The current definition policy committed with or applied to the occurrence.</summary>
    public NodeDeathPolicy? OnNodeDeath { get; init; }
}
