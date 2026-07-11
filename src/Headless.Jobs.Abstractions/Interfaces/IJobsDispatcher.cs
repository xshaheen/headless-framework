// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Internal contract for submitting acquired job contexts to the Jobs thread pool for execution.
/// </summary>
internal interface IJobsDispatcher
{
    /// <summary>
    /// <see langword="true"/> when background services are registered and the dispatcher is active.
    /// <see langword="false"/> in queue-only mode (<c>DisableBackgroundServices</c> was called), in
    /// which case <see cref="DispatchAsync"/> is a no-op.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Submits the acquired job contexts to the Jobs thread pool for concurrent execution.
    /// </summary>
    /// <param name="contexts">The acquired job contexts to dispatch.</param>
    /// <param name="cancellationToken">Token that can abort dispatch.</param>
    Task DispatchAsync(JobExecutionState[] contexts, CancellationToken cancellationToken = default);
}
