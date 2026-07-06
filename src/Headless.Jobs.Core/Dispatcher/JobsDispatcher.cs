// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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

        foreach (var context in contexts)
        {
            var semaphore = concurrencyGate.GetSemaphoreOrNull(context.FunctionName, context.CachedMaxConcurrency);

            await taskScheduler
                .QueueAsync(
                    async ct =>
                    {
                        if (semaphore != null)
                        {
                            await semaphore.WaitAsync(ct).ConfigureAwait(false);
                        }

                        try
                        {
                            await taskHandler
                                .ExecuteTaskAsync(context, isDue: false, cancellationToken: ct)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            semaphore?.Release();
                        }
                    },
                    context.CachedPriority,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }
}
