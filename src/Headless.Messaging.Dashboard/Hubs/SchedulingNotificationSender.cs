// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace Headless.Messaging.Dashboard.Hubs;

/// <summary>
/// Default implementation of <see cref="ISchedulingNotificationSender"/> using SignalR.
/// </summary>
internal sealed class SchedulingNotificationSender(IHubContext<SchedulingNotificationHub> hubContext)
    : ISchedulingNotificationSender
{
    public async Task JobStatusChangedAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        await hubContext
            .Clients.All.SendAsync(
                "JobStatusChanged",
                new
                {
                    job.Name,
                    Status = job.Status.ToString(),
                    job.NextRunTime,
                    job.IsEnabled,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task ExecutionProgressAsync(JobExecution execution, CancellationToken cancellationToken = default)
    {
        await hubContext
            .Clients.All.SendAsync(
                "ExecutionProgress",
                new
                {
                    execution.Id,
                    execution.JobId,
                    Status = execution.Status.ToString(),
                    execution.DateStarted,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task ExecutionCompletedAsync(
        string jobName,
        JobExecution execution,
        CancellationToken cancellationToken = default
    )
    {
        // Send to all clients
        await hubContext
            .Clients.All.SendAsync(
                "ExecutionCompleted",
                new
                {
                    execution.Id,
                    execution.JobId,
                    JobName = jobName,
                    Status = execution.Status.ToString(),
                    execution.Duration,
                    execution.Error,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Also send to job-specific group
        await hubContext
            .Clients.Group($"job:{jobName}")
            .SendAsync(
                "JobExecutionCompleted",
                new
                {
                    execution.Id,
                    execution.JobId,
                    Status = execution.Status.ToString(),
                    execution.Duration,
                    execution.Error,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
