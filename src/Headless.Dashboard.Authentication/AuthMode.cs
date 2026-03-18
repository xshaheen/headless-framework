namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication modes supported by dashboards.
/// </summary>
public enum AuthMode
{
    /// <summary>
    /// No authentication - public dashboard.
    /// </summary>
    None = 0,

    /// <summary>
    /// Basic authentication with username/password.
    /// </summary>
    Basic = 1,

    /// <summary>
    /// API key authentication (sent as Bearer token).
    /// </summary>
    ApiKey = 2,

    /// <summary>
    /// Use host application's authentication.
    /// </summary>
    Host = 3,

    /// <summary>
    /// Custom authentication function.
    /// </summary>
    Custom = 4,
}
