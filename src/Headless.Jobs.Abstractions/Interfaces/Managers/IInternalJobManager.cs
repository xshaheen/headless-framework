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
    Task<T?> GetRequestAsync<T>(Guid jobId, JobType type, CancellationToken cancellationToken = default);
    Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default);
    Task MigrateDefinedCronJobs((string, string)[] cronExpressions, CancellationToken cancellationToken = default);
    Task DeleteJob(Guid jobId, JobType type, CancellationToken cancellationToken = default);
    Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default);
    Task UpdateSkipTimeJobsWithUnifiedContextAsync(
        InternalFunctionContext[] context,
        CancellationToken cancellationToken = default
    );
}
