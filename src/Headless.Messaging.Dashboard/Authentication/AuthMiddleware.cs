// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard.Authentication;

/// <summary>
/// Authentication middleware that protects Messaging Dashboard API endpoints.
/// </summary>
public sealed class AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger, DashboardOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var apiPrefix = (options.PathMatch + "/api").ToLowerInvariant();

        // Skip authentication for excluded paths
        if (_IsExcludedPath(path, apiPrefix))
        {
            await next(context);
            return;
        }

        // Only protect dashboard API endpoints
        if (!path.StartsWith(apiPrefix, StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        var authService = context.RequestServices.GetRequiredService<IAuthService>();
        var authResult = await authService.AuthenticateAsync(context);

        if (!authResult.IsAuthenticated)
        {
            logger.LogWarning("Authentication failed for {Path}: {Error}", path, authResult.ErrorMessage);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        context.Items["auth.username"] = authResult.Username;
        context.Items["auth.authenticated"] = true;

        await next(context);
    }

    private static bool _IsExcludedPath(string path, string apiPrefix)
    {
        return path.Contains("/assets/", StringComparison.Ordinal)
            || path.EndsWith(".js", StringComparison.Ordinal)
            || path.EndsWith(".css", StringComparison.Ordinal)
            || path.EndsWith(".ico", StringComparison.Ordinal)
            || path.EndsWith(".png", StringComparison.Ordinal)
            || path.EndsWith(".jpg", StringComparison.Ordinal)
            || path.EndsWith(".svg", StringComparison.Ordinal)
            || path.Contains("/negotiate", StringComparison.Ordinal)
            || string.Equals(path, apiPrefix + "/auth/validate", StringComparison.Ordinal)
            || string.Equals(path, apiPrefix + "/auth/info", StringComparison.Ordinal);
    }
}
