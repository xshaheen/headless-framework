// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Headless.Messaging.Dashboard.Hubs;

/// <summary>
/// Sends real-time scheduling notifications via SignalR.
/// </summary>
public interface ISchedulingNotificationSender
{
    /// <summary>Notify all clients that a job's status changed.</summary>
    Task JobStatusChangedAsync(ScheduledJob job, CancellationToken cancellationToken = default);

    /// <summary>Notify all clients about execution progress.</summary>
    Task ExecutionProgressAsync(JobExecution execution, CancellationToken cancellationToken = default);

    /// <summary>Notify subscribers of a specific job about execution completion.</summary>
    Task ExecutionCompletedAsync(string jobName, JobExecution execution, CancellationToken cancellationToken = default);
}
