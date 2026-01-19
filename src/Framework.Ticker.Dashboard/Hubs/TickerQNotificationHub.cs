using Framework.Ticker.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Framework.Ticker.Hubs;

public class TickerQNotificationHub(ILogger<TickerQNotificationHub> logger, IAuthService authService) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        logger.LogDebug("SignalR connection attempt: {ConnectionId}", connectionId);

        // Authenticate the connection using new auth service
        var authResult = await authService.AuthenticateAsync(Context.GetHttpContext()!);

        if (!authResult.IsAuthenticated)
        {
            logger.LogWarning(
                "SignalR authentication failed: {ConnectionId} - {Error}",
                connectionId,
                authResult.ErrorMessage
            );
            Context.Abort();
            return;
        }

        logger.LogInformation(
            "SignalR connection established: {ConnectionId} - User: {Username}",
            connectionId,
            authResult.Username
        );

        // Store user info in connection
        Context.Items["username"] = authResult.Username;
        Context.Items["authenticated"] = true;

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        var username = Context.Items["username"]?.ToString() ?? "unknown";

        logger.LogInformation(
            "SignalR connection disconnected: {ConnectionId} - User: {Username}",
            connectionId,
            username
        );

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinGroup(string groupName)
    {
        if (!_IsAuthenticated())
        {
            await Clients.Caller.SendAsync("Error", "Authentication required");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        var username = Context.Items["username"]?.ToString();

        logger.LogDebug("User {Username} joined group {GroupName}", username, groupName);
        await Clients.Caller.SendAsync("GroupJoined", groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        if (!_IsAuthenticated())
        {
            await Clients.Caller.SendAsync("Error", "Authentication required");
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        var username = Context.Items["username"]?.ToString();

        logger.LogDebug("User {Username} left group {GroupName}", username, groupName);
        await Clients.Caller.SendAsync("GroupLeft", groupName);
    }

    public async Task GetStatus()
    {
        var status = new
        {
            connectionId = Context.ConnectionId,
            authenticated = _IsAuthenticated(),
            username = Context.Items["username"]?.ToString() ?? "anonymous",
            timestamp = DateTime.UtcNow,
        };

        await Clients.Caller.SendAsync("Status", status);
    }

    private bool _IsAuthenticated()
    {
        return Context.Items.ContainsKey("authenticated") && (bool)Context.Items["authenticated"]!;
    }
}
