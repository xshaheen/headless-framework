// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Dispatches scheduled job executions to their registered <see cref="IConsume{TMessage}"/>
/// handlers, resolved by job name via keyed dependency injection.
/// </summary>
/// <remarks>
/// This is an internal infrastructure interface used by the scheduler engine.
/// It is not part of the public API.
/// </remarks>
internal interface IScheduledJobDispatcher
{
    /// <summary>
    /// Dispatches a scheduled job execution to its handler.
    /// </summary>
    /// <param name="job">The scheduled job definition.</param>
    /// <param name="execution">The current execution attempt.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DispatchAsync(ScheduledJob job, JobExecution execution, CancellationToken cancellationToken = default);
}
