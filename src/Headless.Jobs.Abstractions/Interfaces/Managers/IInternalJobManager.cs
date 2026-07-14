// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

internal interface IInternalJobManager
{
    Task<(TimeSpan TimeRemaining, JobExecutionState[] Functions)> GetNextJobs(
        CancellationToken cancellationToken = default
    );
    Task ReleaseAcquiredResources(JobExecutionState[] context, CancellationToken cancellationToken = default);
    Task<JobExecutionState[]> SetTickersInProgress(
        JobExecutionState[] context,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes the job's current status to the durable store, fenced on ownership + non-terminal status. Returns the
    /// affected row count: <c>0</c> means the write was fenced out (the row was reclaimed/terminalized by a sweep, e.g.
    /// after a stall) and the recorded status may not reflect the actual outcome — callers completing a job
    /// successfully use this to flag the divergence (#462).
    /// </summary>
    Task<int> UpdateTickerAsync(JobExecutionState context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews the running job's lease (#316), dispatching to the time or cron occurrence renew by
    /// <see cref="JobExecutionState.Type"/>. Returns the affected row count: <c>0</c> means the lease was
    /// lost and the caller should cancel the job (cancel-on-loss, U2); a <b>negative</b> value means coordination
    /// membership is not currently established (#461) and the caller should skip the tick rather than cancel.
    /// </summary>
    Task<int> RenewLeaseAsync(JobExecutionState context, CancellationToken cancellationToken = default);
    Task<T?> GetRequestAsync<T>(Guid jobId, JobType type, CancellationToken cancellationToken = default);
    Task<JobExecutionState[]> RunTimedOutTickers(CancellationToken cancellationToken = default);
    Task MigrateDefinedCronJobs((string, string)[] cronExpressions, CancellationToken cancellationToken = default);
    Task DeleteJob(Guid jobId, JobType type, CancellationToken cancellationToken = default);
    Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims jobs stuck <c>InProgress</c> whose lease lapsed, independent of node death (#316/U3). Runs on the
    /// fallback cadence so a job stalled on a still-live node is recovered within ≈ one lease TTL.
    /// </summary>
    Task<int> ReclaimStalledResources(CancellationToken cancellationToken = default);
    Task UpdateSkipTimeJobsWithUnifiedContextAsync(
        JobExecutionState[] context,
        CancellationToken cancellationToken = default
    );
}
