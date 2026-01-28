using Headless.Ticker.Interfaces;
using Headless.Ticker.Models;
using Headless.Ticker.TickerQThreadPool;

namespace Headless.Ticker.Dispatcher;

internal class TickerQDispatcher(TickerQTaskScheduler taskScheduler, TickerExecutionTaskHandler taskHandler)
    : ITickerQDispatcher
{
    private readonly TickerQTaskScheduler _taskScheduler =
        taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
    private readonly TickerExecutionTaskHandler _taskHandler =
        taskHandler ?? throw new ArgumentNullException(nameof(taskHandler));

    public bool IsEnabled => true;

    public async Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
    {
        if (contexts == null || contexts.Length == 0)
        {
            return;
        }

        foreach (var context in contexts)
        {
            await _taskScheduler
                .QueueAsync(
                    async ct => await _taskHandler.ExecuteTaskAsync(context, false, ct).AnyContext(),
                    context.CachedPriority,
                    cancellationToken
                )
                .AnyContext();
        }
    }
}
