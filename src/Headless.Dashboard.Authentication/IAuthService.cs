using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication service interface for dashboards.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Check if the request is authenticated.
    /// </summary>
    Task<AuthResult> AuthenticateAsync(HttpContext context);

    /// <summary>
    /// Get authentication configuration for frontend.
    /// </summary>
    AuthInfo GetAuthInfo();
}

/// <summary>
/// Authentication result.
/// </summary>
public sealed class AuthResult
{
    public bool IsAuthenticated { get; init; }
    public string? Username { get; init; }
    public string? ErrorMessage { get; init; }

    public static AuthResult Success(string? username = null) =>
        new() { IsAuthenticated = true, Username = username ?? "user" };

    public static AuthResult Failure(string? errorMessage = null) =>
        new() { IsAuthenticated = false, ErrorMessage = errorMessage ?? "Authentication failed" };
}

/// <summary>
/// Authentication information for frontend.
/// </summary>
public sealed class AuthInfo
{
    public AuthMode Mode { get; init; }
    public bool IsEnabled { get; init; }
    public int SessionTimeoutMinutes { get; init; }
}
