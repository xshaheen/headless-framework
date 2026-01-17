using Framework.Ticker.Utilities.Entities.BaseEntity;

namespace Framework.Ticker.Utilities.Entities;

public class CronTickerEntity : BaseTickerEntity
{
    public virtual string Expression { get; set; } = null!;
    public virtual byte[]? Request { get; set; }
    public virtual int Retries { get; set; }
    public virtual int[]? RetryIntervals { get; set; }
}
