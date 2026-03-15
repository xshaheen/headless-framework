using Headless.Jobs.Interfaces;

namespace Headless.Jobs;

internal class TickerSystemClock : ITickerClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
