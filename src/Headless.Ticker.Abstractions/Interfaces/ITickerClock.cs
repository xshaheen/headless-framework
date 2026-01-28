namespace Headless.Ticker.Interfaces;

public interface ITickerClock
{
    DateTime UtcNow { get; }
}
