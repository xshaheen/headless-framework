// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Authentication configuration for dashboards.
/// </summary>
/// <remarks>
/// Credential completeness for the selected <see cref="Mode"/> is enforced by the internal
/// FluentValidation validator wired into the options pipeline (validate-on-start) by every
/// <c>AddDashboardAuthentication</c> overload — there is no imperative validation entry point.
/// </remarks>
[PublicAPI]
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
}

/// <summary>
/// FluentValidation validator for <see cref="AuthConfig"/>. Enforces that the credential required by
/// the selected <see cref="AuthMode"/> is present. Wired into the options pipeline (with validation on
/// start) by <see cref="SetupDashboardAuthentication"/> — the single validation point every
/// <see cref="AuthConfig"/> construction path traverses.
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
