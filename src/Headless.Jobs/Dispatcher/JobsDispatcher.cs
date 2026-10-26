// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.JobsThreadPool;

namespace Headless.Jobs.Dispatcher;

internal sealed class JobsDispatcher(
    JobsTaskScheduler taskScheduler,
    JobsExecutionTaskHandler taskHandler,
    IJobFunctionConcurrencyGate concurrencyGate
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
        // already durably InProgress with a lease, so the running job follows the scheduler's execution lifetime.
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
                        ct => taskHandler.ExecuteImmediateTaskAsync(context, ct),
                        context.CachedPriority,
                        capacityCancellationToken: cancellationToken,
                        executionCancellationToken: executionToken,
                        concurrencyGate: semaphore
                    )
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException) when (taskScheduler.IsFrozen)
            {
                // The rows are already persisted and claimed, so the enqueue succeeded: a frozen pool (a stopping
                // host, or one that has not started processing) only means this node will not run them. Hand each
                // one back uncharged, Idle and due, instead of leaving it running for a reclaim that charges a retry.
                for (var j = i; j < contexts.Length; j++)
                {
                    await taskHandler.ReleaseUndispatchedAsync(contexts[j]).ConfigureAwait(false);
                }

                return;
            }
        }
    }
}
