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
    /// Restarts the scheduler only when the occurrence at <paramref name="dueAtStoreUtc"/> is due sooner than the
    /// wake the loop is currently sleeping towards, avoiding redundant restarts.
    /// </summary>
    /// <param name="dueAtStoreUtc">
    /// The candidate occurrence <b>in the store's clock domain</b>, or <see langword="null"/> to skip. This is the
    /// same domain every due time in the subsystem lives in: a time job's execution time, a definition's persisted
    /// <c>NextDueUtc</c>, a released child's re-stamped time. The scheduler converts to local time once, against the
    /// node/store offset it observed on its last poll; callers must not convert, and must never pass an instant they
    /// derived from this node's clock when the store's projection is available.
    /// </param>
    void RestartIfNeeded(DateTime? dueAtStoreUtc);

    /// <summary>Unconditionally restarts the scheduler loop immediately.</summary>
    void Restart();
}
