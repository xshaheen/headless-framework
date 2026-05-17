using Headless.Jobs.Interfaces;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;

namespace Headless.Jobs.Dispatcher;

internal class JobsDispatcher(
    JobsTaskScheduler taskScheduler,
    JobsExecutionTaskHandler taskHandler,
    IJobFunctionConcurrencyGate concurrencyGate
) : IJobsDispatcher
{
    public bool IsEnabled => true;

    public async Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
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
                            await taskHandler.ExecuteTaskAsync(context, false, ct).ConfigureAwait(false);
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
