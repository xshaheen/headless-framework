// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

/// <summary>
/// Materialized execution row for a single occurrence of a recurring cron job. One row is created per
/// scheduled tick of the parent <typeparamref name="TCronJob"/> and progresses through the
/// <c>JobStatus</c> lifecycle.
/// </summary>
/// <typeparam name="TCronJob">The concrete cron job definition type that owns this occurrence.</typeparam>
public class CronJobOccurrenceEntity<TCronJob>
    where TCronJob : CronJobEntity
{
    /// <summary>Unique identifier for this occurrence row.</summary>
    public virtual Guid Id { get; set; }

    /// <summary>Current lifecycle state of this occurrence.</summary>
    public virtual JobStatus Status { get; set; }

    /// <summary>
    /// The <c>node@incarnation</c> identifier of the node that claimed and is executing this
    /// occurrence, or <see langword="null"/> when unleased.
    /// </summary>
    public virtual string? OwnerId { get; set; }

    /// <summary>UTC timestamp when this occurrence was scheduled to run.</summary>
    public virtual DateTime ExecutionTime { get; set; }

    /// <summary>Foreign key to the parent <typeparamref name="TCronJob"/> definition row.</summary>
    public virtual Guid CronJobId { get; set; }

    /// <summary>
    /// UTC lease deadline: the occurrence's pickup lease is held until this instant, after which the
    /// lease-expiry self-heal arm of the claim predicate may re-claim it. Stamped as <c>now + LeaseDuration</c>
    /// using the injected <see cref="TimeProvider"/> (application clock, not the DB server clock). Null means unleased.
    /// </summary>
    public virtual DateTime? LockedUntil { get; set; }
    public virtual DateTime? ExecutedAt { get; set; }

    /// <summary>
    /// Policy applied when the owning node dies mid-execution. Gates the claim predicate's lease-expiry arm
    /// (only <see cref="NodeDeathPolicy.Retry"/> is speculatively re-claimable) and drives the dead-node
    /// sweep's terminal transitions. Defaults to <see cref="NodeDeathPolicy.Retry"/>.
    /// </summary>
    public virtual NodeDeathPolicy OnNodeDeath { get; set; } = NodeDeathPolicy.Retry;

    /// <summary>Navigation property to the parent cron job definition.</summary>
    public virtual TCronJob CronJob { get; set; } = null!;

    /// <summary>Serialized exception message when the occurrence ended in <c>Failed</c> status.</summary>
    public virtual string? ExceptionMessage { get; set; }

    /// <summary>Human-readable reason when the occurrence was skipped.</summary>
    public virtual string? SkippedReason { get; set; }

    /// <summary>Wall-clock execution duration in milliseconds, set after the function completes.</summary>
    public virtual long ElapsedTime { get; set; }

    /// <summary>Number of retry attempts consumed so far for this occurrence.</summary>
    public virtual int RetryCount { get; set; }

    /// <summary>UTC timestamp when this occurrence row was first created.</summary>
    public virtual DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp of the most recent update to this occurrence row.</summary>
    public virtual DateTime UpdatedAt { get; set; }
}
