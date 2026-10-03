// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

internal static class JobsAdmissionWorkItem
{
    /// <summary>Builds the claim-then-execute delegate both scheduler services queue: the admission-time
    /// Queued -> InProgress claim happens inside the worker (single-winner fence), with claim failures and
    /// lost races logged because the worker pool swallows delegate exceptions.</summary>
    public static Func<CancellationToken, Task> Create(
        IInternalJobManager internalJobsManager,
        JobsExecutionTaskHandler taskHandler,
        ILogger logger,
        SemaphoreSlim? semaphore,
        JobExecutionState function,
        bool isDue
    )
    {
        return async ct =>
        {
            if (semaphore != null)
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
            }

            try
            {
                JobExecutionState[] claimed;
                try
                {
                    claimed = await internalJobsManager.SetTickersInProgress([function], ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The worker pool swallows delegate exceptions; without this log a failed claim write would
                    // leave the row Queued with zero operator signal until the fallback sweep re-claims it after
                    // its lease lapses.
                    logger.LogJobAdmissionClaimFailed(ex, function.JobId, function.FunctionName);
                    return;
                }

                if (claimed.Length == 0)
                {
                    logger.LogJobAdmissionClaimLost(function.JobId, function.FunctionName);
                    return;
                }

                await taskHandler
                    .ExecuteTaskAsync(claimed[0], isDue: isDue, cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                semaphore?.Release();
            }
        };
    }
}
