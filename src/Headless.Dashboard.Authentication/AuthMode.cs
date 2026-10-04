// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Specifies authentication modes supported by dashboards.
/// </summary>
[PublicAPI]
public enum AuthMode
{
    /// <summary>
    /// Disables authentication for a public dashboard.
    /// </summary>
    None = 0,

    /// <summary>
    /// Uses basic authentication with a username and password.
    /// </summary>
    Basic = 1,

    /// <summary>
    /// Uses API key authentication passed as a bearer token.
    /// </summary>
    ApiKey = 2,

    /// <summary>
    /// Uses authentication from the host application.
    /// </summary>
    Host = 3,

    /// <summary>
    /// Uses a custom authentication delegate.
    /// </summary>
    Custom = 4,
}
