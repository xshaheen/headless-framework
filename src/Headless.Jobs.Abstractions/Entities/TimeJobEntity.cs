using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Entities;

public class TimeJobEntity : TimeJobEntity<TimeJobEntity>;

public class TimeJobEntity<TTicker> : BaseJobEntity
    where TTicker : TimeJobEntity<TTicker>
{
    public virtual JobStatus Status { get; internal set; }
    public virtual string? LockHolder { get; internal set; }
    public virtual byte[]? Request { get; set; }
    public virtual DateTime? ExecutionTime { get; set; }
    public virtual DateTime? LockedAt { get; internal set; }
    public virtual DateTime? ExecutedAt { get; internal set; }
    public virtual string? ExceptionMessage { get; internal set; }
    public virtual string? SkippedReason { get; internal set; }
    public virtual long ElapsedTime { get; internal set; }
    public virtual int Retries { get; set; }
    public virtual int RetryCount { get; internal set; }
    public virtual int[]? RetryIntervals { get; set; }
    public virtual Guid? ParentId { get; internal set; }

    [JsonIgnore]
    public virtual TTicker? Parent { get; internal set; }
    public virtual ICollection<TTicker> Children { get; set; } = new List<TTicker>();
    public virtual RunCondition? RunCondition { get; set; }
}
