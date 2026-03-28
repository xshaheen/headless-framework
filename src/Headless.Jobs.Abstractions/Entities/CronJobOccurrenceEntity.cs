using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

public class CronJobOccurrenceEntity<TCronJob>
    where TCronJob : CronJobEntity
{
    public virtual Guid Id { get; set; }
    public virtual JobStatus Status { get; set; }
    public virtual string? LockHolder { get; set; }
    public virtual DateTime ExecutionTime { get; set; }
    public virtual Guid CronJobId { get; set; }
    public virtual DateTime? LockedAt { get; set; }
    public virtual DateTime? ExecutedAt { get; set; }
    public virtual TCronJob CronJob { get; set; } = null!;
    public virtual string? ExceptionMessage { get; set; }
    public virtual string? SkippedReason { get; set; }
    public virtual long ElapsedTime { get; set; }
    public virtual int RetryCount { get; set; }
    public virtual DateTime CreatedAt { get; set; }
    public virtual DateTime UpdatedAt { get; set; }
}
