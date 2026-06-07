// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Pushes live-node membership deltas to the dashboard SignalR hub. Subscribes to the coordination membership
/// event stream and forwards each <see cref="NodeJoined"/>, <see cref="NodeSuspected"/>, <see cref="NodeRecovered"/>,
/// and <see cref="NodeLeft"/> observation as a node-state push, replacing the removed Redis-driven heartbeat feed.
/// </summary>
/// <remarks>
/// Only the coordinated dashboard path needs this bridge; the in-memory / single-process path registers no real
/// <see cref="INodeMembership"/>. The bridge is registered unconditionally but no-ops when membership is absent or
/// the <see cref="NullNodeMembership"/> default is present, so the zero-infra dashboard path never fails. The event
/// stream is best-effort acceleration (origin §4b); the authoritative node list is the read endpoint's snapshot.
/// The async enumerator is disposed on stop via <c>await foreach</c> bound to the stopping token.
/// </remarks>
internal sealed class MembershipDashboardBridge(
    INodeMembership membership,
    IJobsNotificationHubSender notificationHubSender,
    ILogger<MembershipDashboardBridge> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing to watch on the no-coordination dashboard path; stay inert until shutdown.
        if (membership is NullNodeMembership)
        {
            return;
        }

        try
        {
            // The await foreach disposes the underlying enumerator on exit/cancellation, releasing the subscription.
            await foreach (var membershipEvent in membership.WatchAsync(stoppingToken).ConfigureAwait(false))
            {
                await HandleEventAsync(membershipEvent).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on host stop.
        }
        catch (Exception ex)
        {
            logger.MembershipDashboardWatchFailed(ex);
        }
    }

    /// <summary>
    /// Handles a single membership event by pushing the changed node's identity and state to the dashboard. A
    /// suspected node is pushed with its <c>Suspected</c> state (not dropped, not treated as a leave); a left node
    /// is pushed with <c>Dead</c> so the panel can render the terminal state.
    /// </summary>
    internal async Task HandleEventAsync(NodeMembershipEvent membershipEvent)
    {
        // The local-only LocalMembershipLost carries no remote node delta for the panel.
        if (membershipEvent is LocalMembershipLost)
        {
            return;
        }

        var state = membershipEvent switch
        {
            NodeSuspected => NodeLivenessState.Suspected,
            NodeLeft => NodeLivenessState.Dead,
            _ => NodeLivenessState.Alive,
        };

        var payload = new
        {
            identity = membershipEvent.Identity.ToString(),
            state = state.ToString(),
            eventType = membershipEvent.GetType().Name,
        };

        await notificationHubSender.UpdateNodesAsync(payload).ConfigureAwait(false);
    }
}

internal static partial class MembershipDashboardBridgeLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "MembershipDashboardWatchFailed",
        Level = LogLevel.Error,
        Message = "Dashboard membership watch loop failed; the live-nodes panel will not receive push updates"
    )]
    public static partial void MembershipDashboardWatchFailed(this ILogger logger, Exception exception);
}
