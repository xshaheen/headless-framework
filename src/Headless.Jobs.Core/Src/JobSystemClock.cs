using Headless.Jobs.Interfaces;

namespace Headless.Jobs;

internal class JobSystemClock : IJobClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
