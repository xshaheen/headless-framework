// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication service supporting 5 modes: None, Basic, ApiKey, Host, Custom.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly AuthConfig _config;
    private readonly ILogger<AuthService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthService"/> and validates <paramref name="config"/>
    /// immediately so misconfiguration is caught at startup rather than on the first request.
    /// </summary>
    /// <param name="config">The authentication configuration, including mode and credentials.</param>
    /// <param name="logger">The logger used to record authentication errors.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown by <see cref="AuthConfig.Validate"/> when the configuration is incomplete for the
    /// selected <see cref="AuthMode"/>.
    /// </exception>
    public AuthService(AuthConfig config, ILogger<AuthService> logger)
    {
        _config = config;
        _logger = logger;
        _config.Validate();
    }

    /// <inheritdoc/>
    public async Task<AuthResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // No authentication required
            if (!_config.IsEnabled)
            {
                return AuthResult.Success("anonymous");
            }

            // Authentication performed by host application
            if (_config.Mode == AuthMode.Host)
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
            return _config.Mode switch
            {
                AuthMode.Basic => await _AuthenticateBasicAsync(authHeader),
                AuthMode.ApiKey => await _AuthenticateApiKeyAsync(authHeader),
                AuthMode.Custom => await _AuthenticateCustomAsync(authHeader, context.RequestServices),
                _ => AuthResult.Failure("Invalid authentication mode"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogAuthenticationError(ex);
            return AuthResult.Failure("Authentication error");
        }
    }

    /// <inheritdoc/>
    public AuthInfo GetAuthInfo()
    {
        return new AuthInfo
        {
            Mode = _config.Mode,
            IsEnabled = _config.IsEnabled,
            SessionTimeoutMinutes = _config.SessionTimeoutMinutes,
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

            if (
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(credentials),
                    Encoding.UTF8.GetBytes(_config.BasicCredentials ?? string.Empty)
                )
            )
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

            if (
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(token),
                    Encoding.UTF8.GetBytes(_config.ApiKey ?? string.Empty)
                )
            )
            {
                return Task.FromResult(AuthResult.Success("api-user"));
            }

            return Task.FromResult(AuthResult.Failure("Invalid token"));
        }
        // ERP022: Authentication should return failure results, not throw exceptions.
#pragma warning disable ERP022
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
            if (_config.CustomValidator?.Invoke(authHeader, serviceProvider) == true)
            {
                return Task.FromResult(AuthResult.Success("custom-user"));
            }

            return Task.FromResult(AuthResult.Failure("Custom authentication failed"));
        }
        // ERP022: Authentication should return failure results, not throw exceptions.
#pragma warning disable ERP022
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
