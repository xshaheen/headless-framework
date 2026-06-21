// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

internal interface IInternalJobManager
{
    Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextJobs(
        CancellationToken cancellationToken = default
    );
    Task ReleaseAcquiredResources(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
    Task SetTickersInProgress(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
    Task UpdateTickerAsync(InternalFunctionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews the running job's lease (#316), dispatching to the time or cron occurrence renew by
    /// <see cref="InternalFunctionContext.Type"/>. Returns the affected row count: <c>0</c> means the lease was
    /// lost and the caller should cancel the job (cancel-on-loss, U2).
    /// </summary>
    Task<int> RenewLeaseAsync(InternalFunctionContext context, CancellationToken cancellationToken = default);
    Task<T?> GetRequestAsync<T>(Guid jobId, JobType type, CancellationToken cancellationToken = default);
    Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default);
    Task MigrateDefinedCronJobs((string, string)[] cronExpressions, CancellationToken cancellationToken = default);
    Task DeleteJob(Guid jobId, JobType type, CancellationToken cancellationToken = default);
    Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims jobs stuck <c>InProgress</c> whose lease lapsed, independent of node death (#316/U3). Runs on the
    /// fallback cadence so a job stalled on a still-live node is recovered within ≈ one lease TTL.
    /// </summary>
    Task<int> ReclaimStalledResources(CancellationToken cancellationToken = default);
    Task UpdateSkipTimeJobsWithUnifiedContextAsync(
        InternalFunctionContext[] context,
        CancellationToken cancellationToken = default
    );
}
