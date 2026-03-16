using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

namespace Headless.Jobs.Temps;

/// <summary>
/// No-operation implementation of IJobsDispatcher.
/// Used when background services are disabled (queue-only mode).
/// </summary>
internal class NoOpJobsDispatcher : IJobsDispatcher
{
    public bool IsEnabled => false;

    public Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
    {
        // No-op: dispatcher not available in queue-only mode
        return Task.CompletedTask;
    }
}
