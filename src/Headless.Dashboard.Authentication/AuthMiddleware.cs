using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication middleware that only protects API endpoints.
/// Static files, negotiate, and auth endpoints are excluded.
/// </summary>
public sealed class AuthMiddleware
{
    /// <summary>Key used to store the authenticated username in <see cref="HttpContext.Items"/>.</summary>
    public const string UsernameKey = "auth.username";

    /// <summary>Key used to store the authentication flag in <see cref="HttpContext.Items"/>.</summary>
    public const string AuthenticatedKey = "auth.authenticated";

    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Skip authentication for:
        // 1. Static files (handled by static files middleware)
        // 2. SignalR negotiate endpoint (anonymous by design)
        // 3. Auth validation endpoint (to avoid circular dependency)
        if (_IsExcludedPath(path))
        {
            await _next(context);
            return;
        }

        // Only protect API endpoints
        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            await _next(context);
            return;
        }

        // Resolve auth service from request scope
        var authService = context.RequestServices.GetRequiredService<IAuthService>();

        // Authenticate the request
        var authResult = await authService.AuthenticateAsync(context);

        if (!authResult.IsAuthenticated)
        {
            // Log path only (no query string) — access_token may be in query params.
            _logger.LogWarning(
                "Authentication failed for {Path}: {Error}",
                context.Request.Path,
                authResult.ErrorMessage
            );
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        // Set user information for downstream middleware
        context.Items[UsernameKey] = authResult.Username;
        context.Items[AuthenticatedKey] = true;

        await _next(context);
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
