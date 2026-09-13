// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// One unit of post-commit work handed from a coordinated job write to <see cref="JobsPostCommitSignalService" />:
/// immediate dispatch acquisition, scheduler restart, and dashboard notification for rows that are already durable.
/// </summary>
/// <remarks>
/// A signal is best-effort acceleration, never the correctness path: the committed row is what the scheduler's poll
/// sweep recovers, so a dropped, abandoned, or faulted signal only delays pickup until the next sweep. Concrete kinds
/// (time jobs committed, cron jobs committed, schedule changed) are owned by the manager that produces them.
/// </remarks>
/// <param name="JobScope">The job identity the worker logs a failure against (an id, or a batch description).</param>
internal abstract record JobsPostCommitSignal(string JobScope)
{
    /// <summary>Runs the side effects for this signal.</summary>
    /// <param name="now">
    /// The clock as read by the worker when it picked the signal up. A commit can land long after the enqueue, so
    /// a due-time decision made from the enqueue-time clock would push a job that is due by now into the poll-sweep
    /// path.
    /// </param>
    /// <param name="cancellationToken">
    /// Fires when the worker's per-signal deadline elapses or the host's shutdown budget is exhausted.
    /// </param>
    public abstract Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
