namespace Headless.Ticker.Interfaces;

public interface ITickerQHostScheduler
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void RestartIfNeeded(DateTime? dateTime);
    void Restart();
}
