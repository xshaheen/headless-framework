using Headless.Ticker.Interfaces;

namespace Headless.Ticker;

internal class TickerSystemClock : ITickerClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
