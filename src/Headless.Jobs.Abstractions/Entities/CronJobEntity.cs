// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

public class CronJobEntity : BaseJobEntity
{
    public virtual string Expression { get; set; } = null!;
    public virtual byte[]? Request { get; set; }
    public virtual int Retries { get; set; }
    public virtual int[]? RetryIntervals { get; set; }

    /// <summary>
    /// Policy applied to this cron job's occurrences when their owning node dies. Propagated to each
    /// generated <see cref="CronJobOccurrenceEntity{TCronJob}"/> at materialization. Defaults to <see cref="NodeDeathPolicy.Retry"/>.
    /// </summary>
    public virtual NodeDeathPolicy OnNodeDeath { get; set; } = NodeDeathPolicy.Retry;
}
