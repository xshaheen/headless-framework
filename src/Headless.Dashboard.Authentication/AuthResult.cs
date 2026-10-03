// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard.Authentication;

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
