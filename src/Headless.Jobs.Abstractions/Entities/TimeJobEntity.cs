// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

public class TimeJobEntity : TimeJobEntity<TimeJobEntity>;

public class TimeJobEntity<TTicker> : BaseJobEntity
    where TTicker : TimeJobEntity<TTicker>
{
    public virtual JobStatus Status { get; internal set; }
    public virtual string? OwnerId { get; internal set; }
    public virtual byte[]? Request { get; set; }
    public virtual DateTime? ExecutionTime { get; set; }

    /// <summary>
    /// UTC lease deadline: the row's pickup lease is held until this instant, after which the lease-expiry
    /// self-heal arm of the claim predicate may re-claim it. Stamped as <c>now + LeaseDuration</c> using the
    /// injected <see cref="TimeProvider"/> (application clock, not the DB server clock). Null means unleased.
    /// </summary>
    public virtual DateTime? LockedUntil { get; internal set; }
    public virtual DateTime? ExecutedAt { get; internal set; }

    /// <summary>
    /// Policy applied when the owning node dies mid-execution. Gates the claim predicate's lease-expiry arm
    /// (only <see cref="NodeDeathPolicy.Retry"/> is speculatively re-claimable) and drives the dead-node
    /// sweep's terminal transitions. Defaults to <see cref="NodeDeathPolicy.Retry"/>.
    /// </summary>
    public virtual NodeDeathPolicy OnNodeDeath { get; set; } = NodeDeathPolicy.Retry;
    public virtual string? ExceptionMessage { get; internal set; }
    public virtual string? SkippedReason { get; internal set; }
    public virtual long ElapsedTime { get; internal set; }
    public virtual int Retries { get; set; }
    public virtual int RetryCount { get; internal set; }
    public virtual int[]? RetryIntervals { get; set; }
    public virtual Guid? ParentId { get; internal set; }

    [JsonIgnore]
    public virtual TTicker? Parent { get; internal set; }
    public virtual ICollection<TTicker> Children { get; set; } = [];
    public virtual RunCondition? RunCondition { get; set; }
}
