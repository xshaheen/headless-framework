// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard.Hubs;

/// <summary>
/// SignalR hub for real-time scheduling dashboard updates.
/// Clients connect to receive job status changes and execution progress.
/// </summary>
public sealed class SchedulingNotificationHub(IServiceProvider serviceProvider) : Hub
{
    /// <summary>
    /// Subscribe to updates for a specific job.
    /// </summary>
    public async Task SubscribeToJob(string jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            throw new HubException("Job name is required.");
        }

        // Validate job exists before allowing subscription
        var storage = serviceProvider.GetService<IScheduledJobStorage>();
        if (storage is not null)
        {
            var job = await storage.GetJobByNameAsync(jobName);
            if (job is null)
            {
                throw new HubException($"Job '{jobName}' not found.");
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"job:{jobName}");
    }

    /// <summary>
    /// Unsubscribe from updates for a specific job.
    /// </summary>
    public async Task UnsubscribeFromJob(string jobName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job:{jobName}");
    }
}
