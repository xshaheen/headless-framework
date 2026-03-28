using Headless.Jobs.Entities.BaseEntity;

namespace Headless.Jobs.Entities;

public class CronJobEntity : BaseJobEntity
{
    public virtual string Expression { get; set; } = null!;
    public virtual byte[]? Request { get; set; }
    public virtual int Retries { get; set; }
    public virtual int[]? RetryIntervals { get; set; }
}
