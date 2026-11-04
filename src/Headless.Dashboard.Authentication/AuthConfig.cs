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
    /// Gets or sets the settings that apply under <see cref="AuthMode.Host"/>: the view and operate policies, the
    /// sign-in challenge scheme, and the session's cookie and lifetime. Other modes ignore them.
    /// </summary>
    public DashboardHostAuthenticationOptions Host { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether authentication is enabled.
    /// </summary>
    public bool IsEnabled => Mode != AuthMode.None;

    /// <summary>
    /// Gets a value indicating whether the dashboard can send a signed-out user to the host's sign-in: only host mode
    /// signs in through the host, and only when <see cref="DashboardHostAuthenticationOptions.ChallengeScheme"/> names
    /// the scheme to challenge.
    /// </summary>
    public bool SignInAvailable => Mode == AuthMode.Host && !string.IsNullOrEmpty(Host.ChallengeScheme);
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

        RuleFor(x => x.Host)
            .NotNull()
            .SetValidator(new DashboardHostAuthenticationOptionsValidator())
            .When(x => x.Mode == AuthMode.Host);
    }
}
