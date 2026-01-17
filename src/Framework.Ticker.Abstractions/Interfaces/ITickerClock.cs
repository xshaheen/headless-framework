namespace Framework.Ticker.Utilities.Interfaces;

public interface ITickerClock
{
    DateTime UtcNow { get; }
}
