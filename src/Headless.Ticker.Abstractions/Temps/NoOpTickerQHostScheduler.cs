using Headless.Ticker.Interfaces;

namespace Headless.Ticker.Temps;

/// <summary>
/// No-operation implementation of ITickerQHostScheduler.
/// Used when background services are disabled (queue-only mode).
/// </summary>
internal class NoOpTickerQHostScheduler : ITickerQHostScheduler
{
    public bool IsRunning => false;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void RestartIfNeeded(DateTime? dateTime)
    {
        // No-op: scheduler not running
    }

    public void Restart()
    {
        // No-op: scheduler not running
    }
}
