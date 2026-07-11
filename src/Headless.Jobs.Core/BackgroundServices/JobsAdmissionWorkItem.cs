// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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
    ) =>
        async ct =>
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

// Shared by JobsSchedulerBackgroundService and JobsFallbackBackgroundService — the per-service ILogger
// instance keeps the log category distinct while the message shape stays in one place.
internal static partial class JobsAdmissionClaimLog
{
    [LoggerMessage(
        EventId = 3210,
        Level = LogLevel.Warning,
        Message = "Admission-time claim write for job {JobId} ({FunctionName}) failed; the row stays Queued until the fallback sweep re-claims it after its lease lapses."
    )]
    public static partial void LogJobAdmissionClaimFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        string functionName
    );

    [LoggerMessage(
        EventId = 3211,
        Level = LogLevel.Debug,
        Message = "Admission-time claim for job {JobId} ({FunctionName}) affected no rows (ownership lapsed or another wrapper won); skipping execution."
    )]
    public static partial void LogJobAdmissionClaimLost(this ILogger logger, Guid jobId, string functionName);
}
