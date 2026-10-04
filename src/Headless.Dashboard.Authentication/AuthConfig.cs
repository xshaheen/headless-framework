// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Configures authentication for dashboards.
/// </summary>
/// <remarks>
/// Credential completeness for the selected <see cref="Mode"/> is enforced by the internal
/// FluentValidation validator registered in the options pipeline with validation on start by every
/// <c>AddDashboardAuthentication</c> overload.
/// </remarks>
[PublicAPI]
public sealed class AuthConfig
{
    /// <summary>
    /// Gets or sets the authentication mode.
    /// </summary>
    public AuthMode Mode { get; set; } = AuthMode.None;

    /// <summary>
    /// Gets or sets basic authentication credentials as a Base64-encoded username and password pair.
    /// </summary>
    public string? BasicCredentials { get; set; }

    /// <summary>
    /// Gets or sets the API key used for bearer token authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a custom validation delegate. Receives the authorization header value and
    /// an <see cref="IServiceProvider"/> to resolve scoped services.
    /// </summary>
    public Func<string, IServiceProvider, bool>? CustomValidator { get; set; }

    /// <summary>
    /// Gets or sets the session timeout in minutes. The default is 60 minutes.
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets the authorization policy name for host mode. When <see langword="null"/>, the default policy is used.
    /// </summary>
    public string? HostAuthorizationPolicy { get; set; }

    /// <summary>
    /// Gets a value indicating whether authentication is enabled.
    /// </summary>
    public bool IsEnabled => Mode != AuthMode.None;
}

/// <summary>
/// Validates <see cref="AuthConfig"/> instances by verifying that credentials required by the selected
/// <see cref="AuthMode"/> are present. Registered with startup validation by <see cref="SetupDashboardAuthentication"/>.
/// </summary>
internal sealed class AuthConfigValidator : AbstractValidator<AuthConfig>
{
    public AuthConfigValidator()
    {
        RuleFor(x => x.BasicCredentials)
            .NotEmpty()
            .When(x => x.Mode == AuthMode.Basic)
            .WithMessage("BasicCredentials is required for Basic authentication mode");

        RuleFor(x => x.ApiKey)
            .NotEmpty()
            .When(x => x.Mode == AuthMode.ApiKey)
            .WithMessage("ApiKey is required for ApiKey authentication mode");

        RuleFor(x => x.CustomValidator)
            .NotNull()
            .When(x => x.Mode == AuthMode.Custom)
            .WithMessage("CustomValidator is required for Custom authentication mode");
    }
}
