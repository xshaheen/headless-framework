// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

public class CronJobOccurrenceEntity<TCronJob>
    where TCronJob : CronJobEntity
{
    public virtual Guid Id { get; set; }
    public virtual JobStatus Status { get; set; }
    public virtual string? OwnerId { get; set; }
    public virtual DateTime ExecutionTime { get; set; }
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
    public virtual TCronJob CronJob { get; set; } = null!;
    public virtual string? ExceptionMessage { get; set; }
    public virtual string? SkippedReason { get; set; }
    public virtual long ElapsedTime { get; set; }
    public virtual int RetryCount { get; set; }
    public virtual DateTime CreatedAt { get; set; }
    public virtual DateTime UpdatedAt { get; set; }
}
