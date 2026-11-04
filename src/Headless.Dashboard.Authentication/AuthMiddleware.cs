// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authenticates requests under <c>/api/</c>, except the <c>/api/auth/info</c> and <c>/api/auth/validate</c> endpoints,
/// and every request routed to a SignalR hub endpoint.
/// </summary>
/// <remarks>
/// Add it with <c>UseMiddleware&lt;AuthMiddleware&gt;(authenticationName)</c>. It authenticates through the
/// <see cref="IAuthService"/> keyed by that name, so each dashboard enforces its own mode and credentials.
/// </remarks>
/// <param name="next">The next middleware delegate in the ASP.NET Core pipeline.</param>
/// <param name="authenticationName">
/// The name the dashboard passed to <c>AddDashboardAuthentication</c>; selects the keyed <see cref="IAuthService"/>.
/// </param>
/// <param name="logger">The logger used to record authentication failures.</param>
[PublicAPI]
public sealed class AuthMiddleware(RequestDelegate next, string authenticationName, ILogger<AuthMiddleware> logger)
{
    /// <summary>Key used to store the authenticated username in <see cref="HttpContext.Items"/>.</summary>
    public const string UsernameKey = "auth.username";

    /// <summary>Key used to store the authentication flag in <see cref="HttpContext.Items"/>.</summary>
    public const string AuthenticatedKey = "auth.authenticated";

    /// <summary>
    /// Processes the request, enforcing authentication on every <c>/api/</c> path except <c>/api/auth/info</c> and
    /// <c>/api/auth/validate</c>, and on every request routed to a SignalR hub endpoint (its negotiate route included).
    /// Other paths (the SPA shell and its static files) pass through untouched.
    /// </summary>
    /// <remarks>
    /// On success, sets <see cref="UsernameKey"/> and <see cref="AuthenticatedKey"/> in
    /// <see cref="HttpContext.Items"/> for downstream middleware. On failure, writes a
    /// <c>401 Unauthorized</c> response and logs the sanitized request path at Warning level.
    /// The query string is not logged to prevent leaking tokens passed through <c>access_token</c>.
    /// </remarks>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // All comparisons below are case-insensitive, so the request path needs no lowercased copy per request.
        var path = context.Request.Path.Value ?? "";

        // API endpoints and hub endpoints are protected here; the SPA shell and its static files are not. SignalR adds
        // a connection to Clients.All before the hub's OnConnectedAsync can refuse it, so a hub request is refused here,
        // before a connection exists to hear a broadcast; routing metadata, not the path, marks it. Inside /api/ only
        // the two auth endpoints the login flow needs are anonymous; matching anything else by extension or substring
        // would let a request such as /api/x/y.js reach a route parameter unauthenticated.
        var isHubRequest = context.GetEndpoint()?.Metadata.GetMetadata<HubMetadata>() is not null;
        if (
            !isHubRequest
            && (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || _IsAnonymousAuthEndpoint(path))
        )
        {
            await next(context);
            return;
        }

        if (!await DashboardRequestAuthentication.TryAuthenticateAsync(context, authenticationName, logger))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await next(context);
    }

    private static bool _IsAnonymousAuthEndpoint(string path)
    {
        return string.Equals(path, "/api/auth/validate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/api/auth/info", StringComparison.OrdinalIgnoreCase);
    }
}
