// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// A read-only snapshot of the current authentication configuration, returned by
/// <see cref="IAuthService.GetAuthInfo"/> for consumption by the dashboard frontend.
/// </summary>
[PublicAPI]
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
