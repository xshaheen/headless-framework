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
    /// <summary>
    /// Six-field (seconds-inclusive) NCrontab expression that drives occurrence generation. Evaluated in
    /// the timezone configured on <c>SchedulerOptionsBuilder.SchedulerTimeZone</c>.
    /// </summary>
    public virtual string Expression { get; set; } = null!;

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
