// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

/// <summary>
/// Persistent definition row for a recurring cron job. One <c>CronJobEntity</c> exists per registered
/// cron function; the scheduler materializes <c>CronJobOccurrenceEntity</c> rows from it on each tick.
/// </summary>
[PublicAPI]
public class CronJobEntity : BaseJobEntity
{
    internal CronJobEntity Clone()
    {
        var clone = (CronJobEntity)MemberwiseClone();
        clone.Request = Request?.ToArray();
        clone.RetryIntervals = RetryIntervals?.ToArray();
        return clone;
    }

    /// <summary>
    /// Six-field (seconds-inclusive) NCrontab expression that drives occurrence generation.
    /// </summary>
    public virtual string Expression { get; set; } = null!;

    /// <summary>
    /// Optional IANA timezone identifier used to evaluate <see cref="Expression"/>. A <see langword="null"/>
    /// value uses the scheduler-global timezone.
    /// </summary>
    public virtual string? TimeZoneId { get; set; }

    /// <summary>Whether this definition is paused and must not materialize or start pending occurrences.</summary>
    public virtual bool IsPaused { get; set; }

    /// <summary>
    /// Monotonic definition version used to fence scheduler work calculated before a pause, resume, or schedule edit.
    /// </summary>
    public virtual long ScheduleRevision { get; set; }

    /// <summary>
    /// UTC instant through which this definition's schedule has been reconciled. Records what was <i>accounted
    /// for</i> rather than what was promised, so a skip advances it without anything firing and it stays true when
    /// a rule change invalidates any derived prediction. Written and compared using the store's clock.
    /// </summary>
    public virtual DateTime ReconciledThroughUtc { get; set; }

    /// <summary>
    /// UTC instant of the first occurrence after <see cref="ReconciledThroughUtc"/>. This is the indexed dispatch
    /// key the scheduler selects on; it is always derivable from the watermark and the definition, so it can be
    /// rebuilt whenever schedule interpretation changes.
    /// </summary>
    /// <remarks>
    /// Two values are reserved sentinels rather than real occurrence instants. <see langword="default"/> means the
    /// position has not been initialized yet — a definition seeded before this field existed, or created by a path
    /// that did not set it; the scheduler initializes it from the store's instant on the next wake rather than from
    /// occurrence history, so no backlog is replayed. <see cref="DateTime.MaxValue"/> means the schedule has no
    /// further occurrence (an exhausted or unparseable expression) and parks the definition beyond any wake instead
    /// of leaving it permanently due.
    /// </remarks>
    public virtual DateTime NextDueUtc { get; set; }

    /// <summary>
    /// Opaque fingerprint of the rules used to derive <see cref="NextDueUtc"/> — cron-library semantics, timezone
    /// rule version, and DST interpretation. Only equality is meaningful. A mismatch means an identical expression
    /// and timezone now resolve to a different instant, which is surfaced and rebased rather than replayed.
    /// </summary>
    public virtual string? EvaluationFingerprint { get; set; }

    /// <summary>
    /// Seconds of lateness tolerated before a single pending occurrence counts as a misfire. Resolved once at
    /// creation from the scheduler-wide setting and persisted here, so every node evaluates the same threshold and
    /// no node's local configuration can decide whether an instant misfired.
    /// </summary>
    public virtual int MissedRunGraceSeconds { get; set; }

    /// <summary>
    /// Policy applied when this definition enters recovery. Seeded from the job function attribute at creation and
    /// never reapplied afterwards, so any later value is an operator override.
    /// </summary>
    /// <remarks>
    /// Not yet honored by the scheduler. Misfire detection and the skip/coalesce recovery behavior this selects ship
    /// in a later slice; until then the value is persisted and returned but changes nothing at runtime. The same
    /// applies to <see cref="MissedRunGraceSeconds"/>.
    /// </remarks>
    public virtual MissedRunPolicy OnMissedRun { get; set; } = MissedRunPolicy.Coalesce;

    /// <summary>
    /// Optional serialized request payload (JSON, optionally GZip-compressed) propagated to every
    /// generated occurrence.
    /// </summary>
    public virtual byte[]? Request { get; set; }

    /// <summary>Maximum number of retry attempts when an occurrence fails. <c>0</c> means no retries.</summary>
    public virtual int Retries { get; set; }

    /// <summary>
    /// Optional per-retry delay intervals in seconds. When shorter than the retry count, the last interval
    /// is repeated for remaining retries.
    /// </summary>
    public virtual int[]? RetryIntervals { get; set; }

    /// <summary>
    /// Policy applied to this cron job's occurrences when their owning node dies. Propagated to each
    /// generated <see cref="CronJobOccurrenceEntity{TCronJob}"/> at materialization. Defaults to <see cref="NodeDeathPolicy.Retry"/>.
    /// </summary>
    public virtual NodeDeathPolicy OnNodeDeath { get; set; } = NodeDeathPolicy.Retry;
}
