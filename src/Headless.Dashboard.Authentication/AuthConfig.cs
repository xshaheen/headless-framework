namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication configuration for dashboards.
/// </summary>
public class AuthConfig
{
    /// <summary>
    /// Authentication mode.
    /// </summary>
    public AuthMode Mode { get; set; } = AuthMode.None;

    /// <summary>
    /// Basic authentication credentials (Base64 encoded username:password).
    /// </summary>
    public string? BasicCredentials { get; set; }

    /// <summary>
    /// API key for authentication (sent as Bearer token).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Custom authentication function.
    /// </summary>
    public Func<string, bool>? CustomValidator { get; set; }

    /// <summary>
    /// Session timeout in minutes (default: 60 minutes).
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Authorization policy name for Host mode (default: null uses the default policy).
    /// </summary>
    public string? HostAuthorizationPolicy { get; set; }

    /// <summary>
    /// Whether authentication is enabled.
    /// </summary>
    public bool IsEnabled => Mode != AuthMode.None;

    /// <summary>
    /// Validate the configuration.
    /// </summary>
    public void Validate()
    {
        switch (Mode)
        {
            case AuthMode.Basic when string.IsNullOrEmpty(BasicCredentials):
                throw new InvalidOperationException("BasicCredentials is required for Basic authentication mode");
            case AuthMode.ApiKey when string.IsNullOrEmpty(ApiKey):
                throw new InvalidOperationException("ApiKey is required for ApiKey authentication mode");
            case AuthMode.Custom when CustomValidator == null:
                throw new InvalidOperationException("CustomValidator is required for Custom authentication mode");
        }
    }
}
