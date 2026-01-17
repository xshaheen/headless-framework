using Framework.Ticker.Utilities.Models;

namespace Framework.Ticker.Utilities.Interfaces;

public interface ITickerQDispatcher
{
    /// <summary>
    /// Indicates whether the dispatcher is functional (background services enabled).
    /// When false, DispatchAsync will be a no-op.
    /// </summary>
    bool IsEnabled { get; }

    Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default);
}
