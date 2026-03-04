// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard.Authentication;

/// <summary>
/// Authentication service handling all 5 auth modes.
/// </summary>
public sealed class AuthService(AuthConfig config, ILogger<AuthService> logger) : IAuthService
{
    public async Task<AuthResult> AuthenticateAsync(HttpContext context)
    {
        try
        {
            if (!config.IsEnabled)
            {
                return AuthResult.Success("anonymous");
            }

            if (config.Mode == AuthMode.Host)
            {
                return await _AuthenticateHostAsync(context);
            }

            var authHeader = _GetAuthorizationValue(context);
            if (string.IsNullOrEmpty(authHeader))
            {
                return AuthResult.Failure("No authorization provided");
            }

            return config.Mode switch
            {
                AuthMode.Basic => await _AuthenticateBasicAsync(authHeader),
                AuthMode.ApiKey => await _AuthenticateApiKeyAsync(authHeader),
                AuthMode.Custom => await _AuthenticateCustomAsync(authHeader),
                _ => AuthResult.Failure("Invalid authentication mode"),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Authentication error");
            return AuthResult.Failure("Authentication error");
        }
    }

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
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader))
        {
            return authHeader;
        }

        // Try access_token query parameter (for SignalR WebSocket)
        var accessToken = context.Request.Query["access_token"].FirstOrDefault();
        if (!string.IsNullOrEmpty(accessToken))
        {
            return accessToken;
        }

        return null;
    }

    private Task<AuthResult> _AuthenticateBasicAsync(string authHeader)
    {
        try
        {
            var credentials = authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                ? authHeader[6..]
                : authHeader;

            if (
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(credentials),
                    Encoding.UTF8.GetBytes(config.BasicCredentials ?? string.Empty)
                )
            )
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credentials));
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
            var token = authHeader switch
            {
                _ when authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) => authHeader[7..],
                _ when authHeader.StartsWith("Bearer:", StringComparison.OrdinalIgnoreCase) => authHeader[7..],
                _ => authHeader,
            };

            if (
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(token),
                    Encoding.UTF8.GetBytes(config.ApiKey ?? string.Empty)
                )
            )
            {
                return Task.FromResult(AuthResult.Success("api-user"));
            }

            return Task.FromResult(AuthResult.Failure("Invalid token"));
        }
#pragma warning disable ERP022 // Authentication should return failure results, not throw exceptions.
        catch
        {
            return Task.FromResult(AuthResult.Failure("Invalid bearer token format"));
        }
#pragma warning restore ERP022
    }

    private Task<AuthResult> _AuthenticateCustomAsync(string authHeader)
    {
        try
        {
            if (config.CustomValidator?.Invoke(authHeader) == true)
            {
                return Task.FromResult(AuthResult.Success("custom-user"));
            }

            return Task.FromResult(AuthResult.Failure("Custom authentication failed"));
        }
#pragma warning disable ERP022 // Authentication should return failure results, not throw exceptions.
        catch
        {
            return Task.FromResult(AuthResult.Failure("Custom authentication error"));
        }
#pragma warning restore ERP022
    }

    private static Task<AuthResult> _AuthenticateHostAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var username = context.User.Identity.Name ?? "host-user";
            return Task.FromResult(AuthResult.Success(username));
        }

        return Task.FromResult(AuthResult.Failure("Host authentication required"));
    }
}
