using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Jobs.JobsThreadPool;

namespace Headless.Jobs.Dispatcher;

internal class JobsDispatcher(
    JobsTaskScheduler taskScheduler,
    TickerExecutionTaskHandler taskHandler,
    ITickerFunctionConcurrencyGate concurrencyGate
) : IJobsDispatcher
{
    private readonly JobsTaskScheduler _taskScheduler =
        taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
    private readonly TickerExecutionTaskHandler _taskHandler =
        taskHandler ?? throw new ArgumentNullException(nameof(taskHandler));
    private readonly ITickerFunctionConcurrencyGate _concurrencyGate =
        concurrencyGate ?? throw new ArgumentNullException(nameof(concurrencyGate));

    public bool IsEnabled => true;

    public async Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
    {
        if (contexts == null || contexts.Length == 0)
        {
            return;
        }

        foreach (var context in contexts)
        {
            var semaphore = _concurrencyGate.GetSemaphoreOrNull(context.FunctionName, context.CachedMaxConcurrency);

            await _taskScheduler
                .QueueAsync(
                    async ct =>
                    {
                        if (semaphore != null)
                        {
                            await semaphore.WaitAsync(ct).ConfigureAwait(false);
                        }

                        try
                        {
                            await _taskHandler.ExecuteTaskAsync(context, false, ct).ConfigureAwait(false);
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
