using Framework.Ticker.Utilities.Interfaces;
using Framework.Ticker.Utilities.Models;

namespace Framework.Ticker.Utilities.Temps;

/// <summary>
/// No-operation implementation of ITickerQDispatcher.
/// Used when background services are disabled (queue-only mode).
/// </summary>
internal class NoOpTickerQDispatcher : ITickerQDispatcher
{
    public bool IsEnabled => false;

    public Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
    {
        // No-op: dispatcher not available in queue-only mode
        return Task.CompletedTask;
    }
}
