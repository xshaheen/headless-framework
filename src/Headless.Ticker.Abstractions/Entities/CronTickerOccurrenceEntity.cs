using Headless.Ticker.Enums;

namespace Headless.Ticker.Entities;

public class CronTickerOccurrenceEntity<TCronTicker>
    where TCronTicker : CronTickerEntity
{
    public virtual Guid Id { get; set; }
    public virtual TickerStatus Status { get; set; }
    public virtual string? LockHolder { get; set; }
    public virtual DateTime ExecutionTime { get; set; }
    public virtual Guid CronTickerId { get; set; }
    public virtual DateTime? LockedAt { get; set; }
    public virtual DateTime? ExecutedAt { get; set; }
    public virtual TCronTicker CronTicker { get; set; } = null!;
    public virtual string? ExceptionMessage { get; set; }
    public virtual string? SkippedReason { get; set; }
    public virtual long ElapsedTime { get; set; }
    public virtual int RetryCount { get; set; }
    public virtual DateTime CreatedAt { get; set; }
    public virtual DateTime UpdatedAt { get; set; }
}
