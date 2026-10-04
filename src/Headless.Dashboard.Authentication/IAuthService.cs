// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Defines authentication operations for dashboards.
/// </summary>
/// <remarks>
/// Consumed by <see cref="AuthMiddleware"/> on protected API requests. Implementations
/// are resolved from the request service scope so they can use scoped dependencies.
/// </remarks>
[PublicAPI]
public interface IAuthService
{
    /// <summary>
    /// Authenticates the HTTP request against the configured <see cref="AuthMode"/>.
    /// </summary>
    /// <param name="context">The HTTP context whose headers and user principal are inspected.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// An <see cref="AuthResult"/> indicating success with an optional username, or failure
    /// with an error description. Implementations return failure instead of throwing.
    /// </returns>
    Task<AuthResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a snapshot of the active authentication configuration for the dashboard frontend.
    /// </summary>
    /// <returns>An <see cref="AuthInfo"/> describing the active mode and session settings.</returns>
    AuthInfo GetAuthInfo();
}

/// <summary>
/// Represents the outcome of an authentication attempt.
/// </summary>
[PublicAPI]
public sealed class AuthResult
{
    /// <summary>
    /// Gets a value indicating whether authentication succeeded.
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// Gets the authenticated username when <see cref="IsAuthenticated"/> is <see langword="true"/>,
    /// or <see langword="null"/> when authentication failed.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Gets a human-readable description of the failure when <see cref="IsAuthenticated"/> is
    /// <see langword="false"/>, or <see langword="null"/> when authentication succeeded.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful <see cref="AuthResult"/> with the specified username.
    /// </summary>
    /// <param name="username">
    /// The authenticated username to record. Defaults to <c>"user"</c> when <see langword="null"/>.
    /// </param>
    /// <returns>An <see cref="AuthResult"/> with <see cref="IsAuthenticated"/> set to <see langword="true"/>.</returns>
    public static AuthResult Success(string? username = null)
    {
        return new() { IsAuthenticated = true, Username = username ?? "user" };
    }

    /// <summary>
    /// Creates a failed <see cref="AuthResult"/> with an optional error message.
    /// </summary>
    /// <param name="errorMessage">
    /// A description of why authentication failed. Defaults to <c>"Authentication failed"</c> when
    /// <see langword="null"/>.
    /// </param>
    /// <returns>An <see cref="AuthResult"/> with <see cref="IsAuthenticated"/> set to <see langword="false"/>.</returns>
    public static AuthResult Failure(string? errorMessage = null)
    {
        return new() { IsAuthenticated = false, ErrorMessage = errorMessage ?? "Authentication failed" };
    }
}

/// <summary>
/// Represents a read-only snapshot of current authentication configuration returned by
/// <see cref="IAuthService.GetAuthInfo"/> for the dashboard frontend.
/// </summary>
[PublicAPI]
public sealed class AuthInfo
{
    /// <summary>
    /// Gets the active authentication mode for this dashboard.
    /// </summary>
    public AuthMode Mode { get; init; }

    /// <summary>
    /// Gets a value indicating whether authentication is enabled when <see cref="Mode"/> is not
    /// <see cref="AuthMode.None"/>.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the session timeout in minutes after which an authenticated session is invalidated.
    /// </summary>
    public int SessionTimeoutMinutes { get; init; }
}
