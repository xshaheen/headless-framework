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
[PublicAPI]
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
