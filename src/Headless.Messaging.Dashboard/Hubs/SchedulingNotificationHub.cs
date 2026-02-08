// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.SignalR;

namespace Headless.Messaging.Dashboard.Hubs;

/// <summary>
/// SignalR hub for real-time scheduling dashboard updates.
/// Clients connect to receive job status changes and execution progress.
/// </summary>
public sealed class SchedulingNotificationHub : Hub
{
    /// <summary>
    /// Subscribe to updates for a specific job.
    /// </summary>
    public async Task SubscribeToJob(string jobName)
    {
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
