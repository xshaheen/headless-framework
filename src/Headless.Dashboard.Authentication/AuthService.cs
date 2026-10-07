// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authenticates requests using one of five modes: None, Basic, ApiKey, Host, or Custom.
/// </summary>
/// <remarks>
/// The service does not re-validate <paramref name="config"/>. Credential completeness is enforced
/// by the FluentValidation validator registered in the options pipeline with validation on start in
/// <see cref="SetupDashboardAuthentication"/>.
/// </remarks>
/// <param name="config">The authentication configuration, including mode and credentials.</param>
/// <param name="logger">The logger used to record authentication errors.</param>
[PublicAPI]
public sealed class AuthService(AuthConfig config, ILogger<AuthService> logger) : IAuthService
{
    // Config is immutable for the service lifetime, so the expected credential bytes are encoded once instead
    // of per authenticated request (FixedTimeEquals needs byte[] on both sides).
    private readonly byte[] _basicCredentialsBytes = Encoding.UTF8.GetBytes(config.BasicCredentials ?? string.Empty);
    private readonly byte[] _apiKeyBytes = Encoding.UTF8.GetBytes(config.ApiKey ?? string.Empty);

    /// <inheritdoc/>
    public async Task<AuthResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // No authentication required
            if (!config.IsEnabled)
            {
                return AuthResult.Success("anonymous");
            }

            // Authentication performed by host application
            if (config.Mode == AuthMode.Host)
            {
                return await _AuthenticateHostAsync(context);
            }

            // Get authorization header or query parameter
            var authHeader = _GetAuthorizationValue(context);
            if (string.IsNullOrEmpty(authHeader))
            {
                return AuthResult.Failure("No authorization provided");
            }

            // Authenticate based on mode
            return config.Mode switch
            {
                AuthMode.Basic => await _AuthenticateBasicAsync(authHeader),
                AuthMode.ApiKey => await _AuthenticateApiKeyAsync(authHeader),
                AuthMode.Custom => await _AuthenticateCustomAsync(authHeader, context.RequestServices),
                _ => AuthResult.Failure("Invalid authentication mode"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogAuthenticationError(ex);
            return AuthResult.Failure("Authentication error");
        }
    }

    /// <inheritdoc/>
    public AuthInfo GetAuthInfo()
    {
        return new AuthInfo
        {
            Mode = config.Mode,
            IsEnabled = config.IsEnabled,
            SessionTimeoutMinutes = config.SessionTimeoutMinutes,
        };
    }

    private static string? _GetAuthorizationValue(HttpContext context)
    {
        // Try Authorization header first
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader))
        {
            return authHeader;
        }

        // Try access_token query parameter only for SignalR paths.
        // Query strings leak to server logs, browser history, and Referer headers,
        // so we restrict this to SignalR endpoints where WebSocket auth requires it.
        var path = context.Request.Path.Value ?? "";
        if (
            path.Contains("/hub", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/negotiate", StringComparison.OrdinalIgnoreCase)
        )
        {
            var accessToken = context.Request.Query["access_token"].FirstOrDefault();
            if (!string.IsNullOrEmpty(accessToken))
            {
                return accessToken;
            }
        }

        return null;
    }

    private Task<AuthResult> _AuthenticateBasicAsync(string authHeader)
    {
        try
        {
            // Handle both "Basic <credentials>" and raw credentials
            var credentials = authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                ? authHeader[6..]
                : authHeader;

            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(credentials), _basicCredentialsBytes))
            {
                // Decode to get username for display
                var decoded = credentials.DecodeBase64();
                var username = decoded.Split(':')[0];
                return Task.FromResult(AuthResult.Success(username));
            }

            return Task.FromResult(AuthResult.Failure("Invalid credentials"));
        }
#pragma warning disable ERP022 // Authentication should return failure results, not throw exceptions.
        catch
        {
            return Task.FromResult(AuthResult.Failure("Invalid basic auth format"));
        }
#pragma warning restore ERP022
    }

    private Task<AuthResult> _AuthenticateApiKeyAsync(string authHeader)
    {
        try
        {
            // Handle both "Bearer <token>", "Bearer:<token>", and raw token
            var token = authHeader switch
            {
                _ when authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) => authHeader[7..],
                _ when authHeader.StartsWith("Bearer:", StringComparison.OrdinalIgnoreCase) => authHeader[7..],
                _ => authHeader,
            };

            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), _apiKeyBytes))
            {
                return Task.FromResult(AuthResult.Success("api-user"));
            }

            return Task.FromResult(AuthResult.Failure("Invalid token"));
        }
#pragma warning disable ERP022 // Any failure rejects the request; authentication reports failure instead of throwing.
        catch
        {
            return Task.FromResult(AuthResult.Failure("Invalid bearer token format"));
        }
#pragma warning restore ERP022
    }

    private Task<AuthResult> _AuthenticateCustomAsync(string authHeader, IServiceProvider serviceProvider)
    {
        try
        {
            if (config.CustomValidator?.Invoke(authHeader, serviceProvider) == true)
            {
                return Task.FromResult(AuthResult.Success("custom-user"));
            }

            return Task.FromResult(AuthResult.Failure("Custom authentication failed"));
        }
#pragma warning disable ERP022 // Any failure, including in the custom validator, rejects the request instead of throwing.
        catch
        {
            return Task.FromResult(AuthResult.Failure("Custom authentication error"));
        }
#pragma warning restore ERP022
    }

    private static Task<AuthResult> _AuthenticateHostAsync(HttpContext context)
    {
        // Delegate to host application's authentication
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var username = context.User.Identity.Name ?? "host-user";
            return Task.FromResult(AuthResult.Success(username));
        }

        return Task.FromResult(AuthResult.Failure("Host authentication required"));
    }
}

internal static partial class AuthServiceLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "AuthenticationError",
        Level = LogLevel.Error,
        Message = "Authentication error"
    )]
    public static partial void LogAuthenticationError(this ILogger logger, Exception exception);
}
