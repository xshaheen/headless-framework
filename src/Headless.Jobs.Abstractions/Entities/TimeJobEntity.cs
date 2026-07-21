// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

/// <summary>
/// Concrete self-referential time job entity for applications that do not need a custom entity type.
/// Equivalent to <c>TimeJobEntity&lt;TimeJobEntity&gt;</c>.
/// </summary>
[PublicAPI]
public class TimeJobEntity : TimeJobEntity<TimeJobEntity>;

/// <summary>
/// Persistent row for a one-shot scheduled (time) job. A time job runs once at its
/// <see cref="ExecutionTime"/> and may carry a chain of child jobs that execute based on
/// the parent's terminal <c>RunCondition</c>.
/// </summary>
/// <typeparam name="TTicker">
/// The concrete derived type used for the self-referential parent/child navigation properties.
/// </typeparam>
[PublicAPI]
public class TimeJobEntity<TTicker> : BaseJobEntity
    where TTicker : TimeJobEntity<TTicker>
{
    /// <summary>Current lifecycle state of this job row.</summary>
    public virtual JobStatus Status { get; internal set; }

    /// <summary>
    /// The <c>node@incarnation</c> identifier of the node that claimed and is executing this job,
    /// or <see langword="null"/> when unleased.
    /// </summary>
    public virtual string? OwnerId { get; internal set; }

    /// <summary>
    /// Optional serialized request payload (JSON, optionally GZip-compressed) for this job execution.
    /// </summary>
    public virtual byte[]? Request { get; set; }

    /// <summary>
    /// UTC time at which the job becomes eligible for dispatch. <see langword="null"/> means dispatch
    /// immediately on enqueue.
    /// </summary>
    public virtual DateTime? ExecutionTime { get; set; }

    /// <summary>
    /// UTC lease deadline: the row's pickup lease is held until this instant, after which the lease-expiry
    /// self-heal arm of the claim predicate may re-claim it. Stamped as <c>now + LeaseDuration</c> using the
    /// provider's time authority (injected <see cref="TimeProvider"/> for in-memory storage; database UTC clock for
    /// relational storage). Null means unleased.
    /// </summary>
    public virtual DateTime? LockedUntil { get; internal set; }

    /// <summary>UTC timestamp when execution completed, or <see langword="null"/> if not yet completed.</summary>
    public virtual DateTime? ExecutedAt { get; internal set; }

    /// <summary>
    /// Whether cooperative cancellation was durably requested for this job. The flag is retained as audit data even
    /// when an executing handler ignores its cancellation token and records its natural terminal result.
    /// </summary>
    public virtual bool CancelRequested { get; internal set; }

    /// <summary>
    /// Policy applied when the owning node dies mid-execution. Gates the claim predicate's lease-expiry arm
    /// (only <see cref="NodeDeathPolicy.Retry"/> is speculatively re-claimable) and drives the dead-node
    /// sweep's terminal transitions. Defaults to <see cref="NodeDeathPolicy.Retry"/>.
    /// </summary>
    public virtual NodeDeathPolicy OnNodeDeath { get; set; } = NodeDeathPolicy.Retry;

    /// <summary>Serialized exception message when the job ended in <c>Failed</c> status.</summary>
    public virtual string? ExceptionMessage { get; internal set; }

    /// <summary>Human-readable reason when the job was skipped.</summary>
    public virtual string? SkippedReason { get; internal set; }

    /// <summary>Wall-clock execution duration in milliseconds, set after the function completes.</summary>
    public virtual long ElapsedTime { get; internal set; }

    /// <summary>Maximum number of retry attempts when the job fails. <c>0</c> means no retries.</summary>
    public virtual int Retries { get; set; }

    /// <summary>Number of retry attempts consumed so far for this job.</summary>
    public virtual int RetryCount { get; internal set; }

    /// <summary>
    /// Optional per-retry delay intervals in seconds. When shorter than the retry count, the last
    /// interval is repeated for remaining retries.
    /// </summary>
    public virtual int[]? RetryIntervals { get; set; }

    /// <summary>
    /// Identifier of the parent job in a chained tree, or <see langword="null"/> for root jobs.
    /// </summary>
    public virtual Guid? ParentId { get; internal set; }

    /// <summary>Navigation property to the parent job in the chain. Excluded from serialization.</summary>
    [JsonIgnore]
    public virtual TTicker? Parent { get; internal set; }

    /// <summary>Child jobs in this chain that run after this job reaches a terminal state.</summary>
    public virtual ICollection<TTicker> Children { get; set; } = [];

    /// <summary>
    /// Condition under which this child job is eligible to run relative to the parent's terminal status.
    /// <see langword="null"/> on root jobs; required on child jobs.
    /// </summary>
    public virtual RunCondition? RunCondition { get; set; }
}
