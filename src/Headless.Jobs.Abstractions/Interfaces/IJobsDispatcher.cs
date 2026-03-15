using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

public interface IJobsDispatcher
{
    /// <summary>
    /// Indicates whether the dispatcher is functional (background services enabled).
    /// When false, DispatchAsync will be a no-op.
    /// </summary>
    bool IsEnabled { get; }

    Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default);
}
