// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

public class JobsNotificationHub(
    ILogger<JobsNotificationHub> logger,
    [FromKeyedServices(DashboardOptionsBuilder.AuthenticationName)] IAuthService authService,
    TimeProvider timeProvider
) : Hub
{
    /// <summary>The hub's path below the dashboard base path.</summary>
    internal const string Path = "/job-notification-hub";

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        logger.ConnectionAttempt(connectionId);

        // Authorization already refused a request without the dashboard's credential or session; this records who
        // connected. Under host authentication the authorization middleware has set the session's user on the request.
        var authResult = await authService.AuthenticateAsync(Context.GetHttpContext()!).ConfigureAwait(false);

        if (!authResult.IsAuthenticated)
        {
            logger.AuthenticationFailed(connectionId, authResult.ErrorMessage);
            Context.Abort();
            return;
        }

        // The user name is often an email; the connection id correlates the entries, and the host's own sign-in logs
        // name the user.
        logger.ConnectionEstablished(connectionId);

        // Store user info in connection
        Context.Items["username"] = authResult.Username;
        Context.Items["authenticated"] = true;

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.ConnectionDisconnected(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    public async Task JoinGroup(string groupName)
    {
        if (!_IsAuthenticated())
        {
            await Clients.Caller.SendAsync("Error", "Authentication required").ConfigureAwait(false);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName).ConfigureAwait(false);
        logger.UserJoinedGroup(Context.ConnectionId, groupName);
        await Clients.Caller.SendAsync("GroupJoined", groupName).ConfigureAwait(false);
    }

    public async Task LeaveGroup(string groupName)
    {
        if (!_IsAuthenticated())
        {
            await Clients.Caller.SendAsync("Error", "Authentication required").ConfigureAwait(false);
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName).ConfigureAwait(false);
        logger.UserLeftGroup(Context.ConnectionId, groupName);
        await Clients.Caller.SendAsync("GroupLeft", groupName).ConfigureAwait(false);
    }

    public async Task GetStatus()
    {
        var status = new
        {
            connectionId = Context.ConnectionId,
            authenticated = _IsAuthenticated(),
            username = Context.Items["username"]?.ToString() ?? "anonymous",
            timestamp = timeProvider.GetUtcNow().UtcDateTime,
        };

        await Clients.Caller.SendAsync("Status", status).ConfigureAwait(false);
    }

    private bool _IsAuthenticated()
    {
        return Context.Items.ContainsKey("authenticated") && (bool)Context.Items["authenticated"]!;
    }
}

internal static partial class JobsNotificationHubLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "SignalR connection attempt: {ConnectionId}")]
    internal static partial void ConnectionAttempt(this ILogger logger, string connectionId);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "SignalR authentication failed: {ConnectionId} - {Error}"
    )]
    internal static partial void AuthenticationFailed(this ILogger logger, string connectionId, string? error);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "SignalR connection established: {ConnectionId}"
    )]
    internal static partial void ConnectionEstablished(this ILogger logger, string connectionId);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "SignalR connection disconnected: {ConnectionId}"
    )]
    internal static partial void ConnectionDisconnected(this ILogger logger, string connectionId);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} joined group {GroupName}"
    )]
    internal static partial void UserJoinedGroup(this ILogger logger, string connectionId, string groupName);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} left group {GroupName}"
    )]
    internal static partial void UserLeftGroup(this ILogger logger, string connectionId, string groupName);
}
