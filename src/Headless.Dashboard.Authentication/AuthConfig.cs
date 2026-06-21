// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication configuration for dashboards.
/// </summary>
public sealed class AuthConfig
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
    /// Custom authentication function. Receives the authorization header value and
    /// an <see cref="IServiceProvider"/> for safe resolution of scoped services.
    /// </summary>
    public Func<string, IServiceProvider, bool>? CustomValidator { get; set; }

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
    /// Validates the configuration and throws when a required credential is missing for the
    /// selected <see cref="Mode"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="Mode"/> is <see cref="AuthMode.Basic"/> and <see cref="BasicCredentials"/>
    /// is null or empty, when <see cref="Mode"/> is <see cref="AuthMode.ApiKey"/> and <see cref="ApiKey"/>
    /// is null or empty, or when <see cref="Mode"/> is <see cref="AuthMode.Custom"/> and
    /// <see cref="CustomValidator"/> is null.
    /// </exception>
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
