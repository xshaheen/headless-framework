namespace Headless.Jobs.Interfaces;

public interface IJobClock
{
    DateTime UtcNow { get; }
}
