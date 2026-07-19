// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication middleware that only protects API endpoints.
/// Static files, negotiate, and auth endpoints are excluded.
/// </summary>
/// <remarks>Initializes a new instance of <see cref="AuthMiddleware"/>.</remarks>
/// <param name="next">The next middleware delegate in the ASP.NET Core pipeline.</param>
/// <param name="logger">The logger used to record authentication failures.</param>
[PublicAPI]
public sealed class AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
{
    /// <summary>Key used to store the authenticated username in <see cref="HttpContext.Items"/>.</summary>
    public const string UsernameKey = "auth.username";

    /// <summary>Key used to store the authentication flag in <see cref="HttpContext.Items"/>.</summary>
    public const string AuthenticatedKey = "auth.authenticated";

    /// <summary>
    /// Processes the request, enforcing authentication on <c>/api/</c> endpoints while passing
    /// through static files, SignalR negotiate, and auth endpoints without challenge.
    /// </summary>
    /// <remarks>
    /// On success, sets <see cref="UsernameKey"/> and <see cref="AuthenticatedKey"/> in
    /// <see cref="HttpContext.Items"/> for downstream middleware. On failure, writes a
    /// <c>401 Unauthorized</c> response and logs the sanitized request path at Warning level.
    /// The query string is never logged to avoid leaking tokens passed via <c>access_token</c>.
    /// </remarks>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Skip authentication for:
        // 1. Static files (handled by static files middleware)
        // 2. SignalR negotiate endpoint (anonymous by design)
        // 3. Auth validation endpoint (to avoid circular dependency)
        if (_IsExcludedPath(path))
        {
            await next(context);
            return;
        }

        // Only protect API endpoints
        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        // Resolve auth service from request scope
        var authService = context.RequestServices.GetRequiredService<IAuthService>();

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

    private static bool _IsExcludedPath(string path)
    {
        return path.Contains("/assets/", StringComparison.Ordinal)
            || path.EndsWith(".js", StringComparison.Ordinal)
            || path.EndsWith(".css", StringComparison.Ordinal)
            || path.EndsWith(".ico", StringComparison.Ordinal)
            || path.EndsWith(".png", StringComparison.Ordinal)
            || path.EndsWith(".jpg", StringComparison.Ordinal)
            || path.EndsWith(".svg", StringComparison.Ordinal)
            || path.EndsWith("/negotiate", StringComparison.Ordinal)
            || string.Equals(path, "/api/auth/validate", StringComparison.Ordinal)
            || string.Equals(path, "/api/auth/info", StringComparison.Ordinal);
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
    public static partial void LogAuthenticationFailed(this ILogger logger, string path, string? error);
}
