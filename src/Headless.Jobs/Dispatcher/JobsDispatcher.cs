// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.BackgroundServices;
using Headless.Jobs.JobsThreadPool;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Dispatcher;

internal sealed class JobsDispatcher(
    JobsTaskScheduler taskScheduler,
    JobsExecutionTaskHandler taskHandler,
    IJobFunctionConcurrencyGate concurrencyGate,
    IInternalJobManager internalJobsManager,
    ILogger<JobsDispatcher> logger
) : IJobsDispatcher
{
    public bool IsEnabled => true;

    public async Task DispatchAsync(JobExecutionState[]? contexts, CancellationToken cancellationToken = default)
    {
        if (contexts == null || contexts.Length == 0)
        {
            return;
        }

        // The caller's token governs only admission (waiting for pool capacity). By dispatch time the row is
        // durably Queued with a lease, and its admission and execution follow the scheduler's execution lifetime.
        // ApplicationStopping fires before hosted services receive their StopAsync drain budget, so using it here
        // would cancel immediate jobs before the scheduler can persist their terminal status.
        var executionToken = taskScheduler.ExecutionCancellationToken;

        for (var i = 0; i < contexts.Length; i++)
        {
            var context = contexts[i];
            var semaphore = concurrencyGate.GetSemaphoreOrNull(context.FunctionName, context.CachedMaxConcurrency);

            try
            {
                await taskScheduler
                    .QueueAsync(
                        JobsAdmissionWorkItem.Create(
                            taskScheduler,
                            internalJobsManager,
                            taskHandler,
                            logger,
                            context,
                            isDue: false,
                            runNow: true
                        ),
                        context.CachedPriority,
                        capacityCancellationToken: cancellationToken,
                        executionCancellationToken: executionToken,
                        concurrencyGate: semaphore,
                        // Refused or abandoned work should be claimable again without waiting for the lease to lapse.
                        notAdmitted: () => _ReleaseUnadmittedClaimsAsync([context]),
                        claim: context
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when ((exception is InvalidOperationException && (taskScheduler.IsFrozen || taskScheduler.IsDisposed))
                    || (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                )
            {
                // The rows are already persisted and claimed, so the enqueue succeeded: a frozen pool (a stopping
                // host, or one that has not started processing) or a caller that stopped waiting for capacity only
                // means this node will not run them. Hand them back so the ordinary claim can start them sooner.
                await _ReleaseUnadmittedClaimsAsync(contexts[i..]).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task _ReleaseUnadmittedClaimsAsync(JobExecutionState[] claims)
    {
        try
        {
            await internalJobsManager.ReleaseAcquiredResources(claims, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogUnadmittedClaimsReleaseFailed(exception, claims.Length);
        }
    }
}

internal static partial class JobsDispatcherLog
{
    [LoggerMessage(
        EventId = 3126,
        Level = LogLevel.Warning,
        Message = "Releasing {ClaimedCount} unadmitted job(s) failed; the fallback sweep can reclaim them when their leases lapse."
    )]
    internal static partial void LogUnadmittedClaimsReleaseFailed(
        this ILogger logger,
        Exception exception,
        int claimedCount
    );
}
