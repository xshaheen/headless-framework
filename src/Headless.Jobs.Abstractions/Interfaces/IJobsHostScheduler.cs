namespace Headless.Jobs.Interfaces;

public interface IJobsHostScheduler
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void RestartIfNeeded(DateTime? dateTime);
    void Restart();
}
