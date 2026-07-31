// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs.Dispatcher;

internal sealed class JobsDispatcher(
    JobsTaskScheduler taskScheduler,
    JobsExecutionTaskHandler taskHandler,
    IJobFunctionConcurrencyGate concurrencyGate,
    IHostApplicationLifetime? hostLifetime = null
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
        // already durably InProgress with a lease, so the running job must be owned by the HOST lifetime — an
        // enqueuing HTTP request ending (cancelled, completed, or its recycled CTS disposed) must not cancel or
        // silently skip a job the store says is running; that row would otherwise sit out its whole lease and be
        // resolved by OnNodeDeath as if the node had died.
        var executionToken = hostLifetime?.ApplicationStopping ?? CancellationToken.None;

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
                    capacityCancellationToken: cancellationToken,
                    executionCancellationToken: executionToken
                )
                .ConfigureAwait(false);
        }
    }
}
