// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

/// <summary>
/// Persistent definition row for a recurring cron job. One <c>CronJobEntity</c> exists per registered
/// cron function; the scheduler materializes <c>CronJobOccurrenceEntity</c> rows from it on each tick.
/// </summary>
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
