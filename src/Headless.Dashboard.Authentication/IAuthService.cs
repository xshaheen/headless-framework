// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication service interface for dashboards.
/// </summary>
/// <remarks>
/// Consumed by <see cref="AuthMiddleware"/> on every protected API request. Implementations
/// are resolved from the request service scope so they may use scoped dependencies.
/// </remarks>
public interface IAuthService
{
    /// <summary>
    /// Authenticates the current HTTP request against the configured <see cref="AuthMode"/>.
    /// </summary>
    /// <param name="context">The current HTTP context whose headers and user principal are inspected.</param>
    /// <param name="cancellationToken">Token to cancel the authentication attempt.</param>
    /// <returns>
    /// An <see cref="AuthResult"/> indicating success (with an optional username) or failure
    /// (with an error description). Implementations should not throw — errors are surfaced through
    /// <see cref="AuthResult.Failure"/>.
    /// </returns>
    Task<AuthResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a snapshot of the current authentication configuration intended for the dashboard
    /// frontend so it can present the correct login UI.
    /// </summary>
    /// <returns>An <see cref="AuthInfo"/> describing the active mode and session settings.</returns>
    AuthInfo GetAuthInfo();
}

/// <summary>
/// Represents the outcome of an authentication attempt.
/// </summary>
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
    /// <see langword="false"/>, or <see langword="null"/> on success.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful <see cref="AuthResult"/> with the given username.
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
/// A read-only snapshot of the current authentication configuration, returned by
/// <see cref="IAuthService.GetAuthInfo"/> for consumption by the dashboard frontend.
/// </summary>
public sealed class AuthInfo
{
    /// <summary>
    /// Gets the active authentication mode for this dashboard.
    /// </summary>
    public AuthMode Mode { get; init; }

    /// <summary>
    /// Gets a value indicating whether authentication is enabled (i.e., <see cref="Mode"/> is not
    /// <see cref="AuthMode.None"/>).
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the session timeout in minutes after which an authenticated session is invalidated.
    /// </summary>
    public int SessionTimeoutMinutes { get; init; }
}
