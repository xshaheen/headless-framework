using Headless.Jobs.Entities.BaseEntity;

namespace Headless.Jobs.Entities;

public class CronTickerEntity : BaseTickerEntity
{
    public virtual string Expression { get; set; } = null!;
    public virtual byte[]? Request { get; set; }
    public virtual int Retries { get; set; }
    public virtual int[]? RetryIntervals { get; set; }
}
