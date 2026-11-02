// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authenticates requests under <c>/api/</c>, except the <c>/api/auth/info</c> and <c>/api/auth/validate</c> endpoints.
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
    /// <c>/api/auth/validate</c>. Paths outside <c>/api/</c> (the SPA shell, static files, and the SignalR hub) pass
    /// through untouched.
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

        // Only API endpoints are protected here: the SPA shell, its static files, and the SignalR hub live outside
        // /api/ (the hub authenticates itself). Inside /api/ only the two auth endpoints the login flow needs are
        // anonymous; matching anything else by extension or substring would let a request such as /api/x/y.js reach
        // a route parameter unauthenticated.
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || _IsAnonymousAuthEndpoint(path))
        {
            await next(context);
            return;
        }

        // Resolve this dashboard's auth service from the request scope
        var authService = context.RequestServices.GetRequiredKeyedService<IAuthService>(authenticationName);

        // Authenticate the request
        var authResult = await authService.AuthenticateAsync(context, context.RequestAborted);

        if (!authResult.IsAuthenticated)
        {
            // Log path only (no query string) — access_token may be in query params.
            // CR/LF stripped so user-controlled path can't inject forged log entries.
            var safePath = _SanitizeForLog(context.Request.Path.Value);
            logger.LogAuthenticationFailed(safePath, authResult.ErrorMessage);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        // Set user information for downstream middleware
        context.Items[UsernameKey] = authResult.Username;
        context.Items[AuthenticatedKey] = true;
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, authResult.Username!)],
                    authenticationType: nameof(AuthMiddleware)
                )
            );
        }

        await next(context);
    }

    private static string _SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    private static bool _IsAnonymousAuthEndpoint(string path)
    {
        return string.Equals(path, "/api/auth/validate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/api/auth/info", StringComparison.OrdinalIgnoreCase);
    }
}

internal static partial class AuthMiddlewareLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "AuthenticationFailed",
        Level = LogLevel.Warning,
        Message = "Authentication failed for {Path}: {Error}"
    )]
    internal static partial void LogAuthenticationFailed(this ILogger logger, string path, string? error);
}
