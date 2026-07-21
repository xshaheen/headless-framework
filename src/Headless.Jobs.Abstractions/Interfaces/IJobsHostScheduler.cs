// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Controls the Jobs background scheduler loop. Inject this interface when the application needs to
/// manually start or stop job processing, for example in response to maintenance windows or when
/// <c>JobsStartMode.Manual</c> is configured.
/// </summary>
[PublicAPI]
public interface IJobsHostScheduler
{
    /// <summary>
    /// <see langword="true"/> when the scheduler background loop is currently active and dispatching jobs.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the scheduler loop if it is not already running.
    /// </summary>
    /// <param name="cancellationToken">Token that can abort the start sequence.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals the scheduler loop to stop and waits for the current iteration to drain.
    /// </summary>
    /// <param name="cancellationToken">Token that can abort the stop wait.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the scheduler only when the next scheduled occurrence at <paramref name="dateTime"/>
    /// is earlier than the scheduler's current wake-up time, avoiding redundant restarts.
    /// </summary>
    /// <param name="dateTime">The candidate next occurrence, or <see langword="null"/> to skip.</param>
    void RestartIfNeeded(DateTime? dateTime);

    /// <summary>Unconditionally restarts the scheduler loop immediately.</summary>
    void Restart();
}
