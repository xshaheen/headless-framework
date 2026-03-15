namespace Headless.Jobs.Interfaces;

public interface ITickerClock
{
    DateTime UtcNow { get; }
}
