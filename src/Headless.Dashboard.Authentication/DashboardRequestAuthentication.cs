// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authenticates one dashboard API request through the dashboard's keyed <see cref="IAuthService"/>. Shared by
/// <see cref="AuthMiddleware"/>, which guards an <c>app.Map</c> branch, and
/// <see cref="DashboardAuthenticationEndpointFilter"/>, which guards a dashboard mounted on the host's endpoints, so
/// both mounts accept the same credentials and expose the same identity to the endpoints.
/// </summary>
internal static class DashboardRequestAuthentication
{
    /// <summary>
    /// Authenticates <paramref name="context"/>. On success, records the username in <see cref="HttpContext.Items"/>
    /// and, when the host has not signed anyone in, sets <see cref="HttpContext.User"/> to the dashboard identity. On
    /// failure, logs the sanitized path; the caller writes the 401.
    /// </summary>
    internal static async Task<bool> TryAuthenticateAsync(
        HttpContext context,
        string authenticationName,
        ILogger logger
    )
    {
        var authService = context.RequestServices.GetRequiredKeyedService<IAuthService>(authenticationName);
        var authResult = await authService.AuthenticateAsync(context, context.RequestAborted).ConfigureAwait(false);

        if (!authResult.IsAuthenticated)
        {
            // Log path only (no query string) — access_token may be in query params.
            // CR/LF stripped so user-controlled path can't inject forged log entries.
            logger.LogAuthenticationFailed(_SanitizeForLog(context.Request.Path.Value), authResult.ErrorMessage);
            return false;
        }

        context.Items[AuthMiddleware.UsernameKey] = authResult.Username;
        context.Items[AuthMiddleware.AuthenticatedKey] = true;
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, authResult.Username!)],
                    authenticationType: nameof(AuthMiddleware)
                )
            );
        }

        return true;
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
