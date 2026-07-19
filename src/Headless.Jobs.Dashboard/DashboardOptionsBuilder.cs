// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Headless.Jobs;

/// <summary>
/// Configuration builder for the Jobs dashboard registered by <c>AddDashboard</c>. Controls the
/// base path, CORS policy, backend domain, authentication mode, middleware hooks, and JSON options
/// for the dashboard SPA and its API endpoints.
/// </summary>
public sealed class DashboardOptionsBuilder
{
    internal string BasePath { get; set; } = "/jobs/dashboard";
    internal Action<CorsPolicyBuilder>? CorsPolicyBuilder { get; set; }
    internal string? BackendDomain { get; set; }

    // Clean authentication system
    internal AuthConfig Auth { get; set; } = new();

    // Tracks whether an authentication mode was explicitly chosen. The dashboard fails closed at startup
    // (see Validate) when this is false, so it cannot ship publicly by omission.
    internal bool AuthConfigured { get; private set; }

    /// <summary>Optional custom middleware inserted into the dashboard pipeline.</summary>
    public Action<IApplicationBuilder>? CustomMiddleware { get; set; }

    /// <summary>Middleware executed before the dashboard request handler.</summary>
    public Action<IApplicationBuilder>? PreDashboardMiddleware { get; set; }

    /// <summary>Middleware executed after the dashboard request handler.</summary>
    public Action<IApplicationBuilder>? PostDashboardMiddleware { get; set; }

    /// <summary>
    /// JsonSerializerOptions specifically for Dashboard API endpoints.
    /// Separate from request serialization options to prevent user configuration from breaking dashboard APIs.
    /// </summary>
    internal JsonSerializerOptions? DashboardJsonOptions { get; set; }

    /// <summary>
    /// Overrides the CORS policy applied to the dashboard endpoints. By default no CORS policy is applied:
    /// the dashboard SPA is served from the same origin as its API, so no cross-origin access is required.
    /// Prefer <see cref="SetCorsOrigins"/> for the common cross-origin case.
    /// </summary>
    /// <param name="corsPolicyBuilder">Callback to configure the CORS policy.</param>
    public DashboardOptionsBuilder SetCorsPolicy(Action<CorsPolicyBuilder> corsPolicyBuilder)
    {
        CorsPolicyBuilder = corsPolicyBuilder;
        return this;
    }

    /// <summary>
    /// Restricts cross-origin access to the given origins with a credentialed policy. Use this when the
    /// dashboard SPA is served from a different origin than its API (see <see cref="SetBackendDomain"/>);
    /// when they share an origin (the default) no CORS configuration is needed.
    /// </summary>
    /// <param name="origins">The exact allowed origins, e.g. <c>https://admin.example.com</c>. Must not be empty.</param>
    public DashboardOptionsBuilder SetCorsOrigins(params string[] origins)
    {
        Argument.IsNotNullOrEmpty(origins);
        CorsPolicyBuilder = cors => cors.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        return this;
    }

    /// <summary>
    /// Sets the URL base path at which the dashboard SPA and its API endpoints are served.
    /// Defaults to <c>/jobs/dashboard</c>.
    /// </summary>
    /// <param name="basePath">The base path, e.g. <c>/admin/jobs</c>.</param>
    public DashboardOptionsBuilder SetBasePath(string basePath)
    {
        BasePath = basePath;
        return this;
    }

    /// <summary>
    /// Sets the backend domain used by the dashboard SPA to construct API URLs when the SPA is
    /// served from a different origin than the API. Leave unset when the SPA and API share an origin.
    /// </summary>
    /// <param name="backendDomain">The API origin, e.g. <c>https://api.example.com</c>.</param>
    public DashboardOptionsBuilder SetBackendDomain(string backendDomain)
    {
        BackendDomain = backendDomain;
        return this;
    }

    /// <summary>
    /// Disables authentication; the dashboard is publicly accessible without credentials.
    /// Only suitable for development or trusted internal networks.
    /// </summary>
    public DashboardOptionsBuilder WithNoAuth()
    {
        Auth.Mode = AuthMode.None;
        AuthConfigured = true;
        return this;
    }

    /// <summary>
    /// Enables HTTP Basic Authentication for the dashboard using the given credentials.
    /// Credentials are Base64-encoded and compared on each request.
    /// </summary>
    /// <param name="username">The required username.</param>
    /// <param name="password">The required password.</param>
    public DashboardOptionsBuilder WithBasicAuth(string username, string password)
    {
        Auth.Mode = AuthMode.Basic;
        Auth.BasicCredentials = $"{username}:{password}".ToBase64();
        AuthConfigured = true;
        return this;
    }

    /// <summary>
    /// Enables API key authentication. The dashboard SPA sends the key as a Bearer token; the
    /// server validates it on each request.
    /// </summary>
    /// <param name="apiKey">The expected API key value.</param>
    public DashboardOptionsBuilder WithApiKey(string apiKey)
    {
        Auth.Mode = AuthMode.ApiKey;
        Auth.ApiKey = apiKey;
        AuthConfigured = true;
        return this;
    }

    /// <summary>
    /// Delegates authentication to the host application's ASP.NET Core authentication middleware.
    /// When <paramref name="policy"/> is specified, the named authorization policy is enforced;
    /// otherwise the default authorization policy applies.
    /// </summary>
    /// <param name="policy">
    /// Optional authorization policy name (e.g., <c>"AdminPolicy"</c>). Pass <see langword="null"/>
    /// or an empty string to use the default policy.
    /// </param>
    public DashboardOptionsBuilder WithHostAuthentication(string? policy = null)
    {
        Auth.Mode = AuthMode.Host;
        Auth.HostAuthorizationPolicy = policy;
        AuthConfigured = true;
        return this;
    }

    /// <summary>
    /// Enables custom authentication by supplying a validation delegate. The delegate receives the
    /// raw Bearer token and the current <see cref="IServiceProvider"/>, and returns
    /// <see langword="true"/> to grant access.
    /// </summary>
    /// <param name="validator">Validation function; must be thread-safe.</param>
    public DashboardOptionsBuilder WithCustomAuth(Func<string, IServiceProvider, bool> validator)
    {
        Auth.Mode = AuthMode.Custom;
        Auth.CustomValidator = validator;
        AuthConfigured = true;
        return this;
    }

    /// <summary>
    /// Sets the idle session timeout for the dashboard. After this period of inactivity the session
    /// cookie is invalidated and the user must re-authenticate.
    /// </summary>
    /// <param name="minutes">Timeout in minutes.</param>
    public DashboardOptionsBuilder WithSessionTimeout(int minutes)
    {
        Auth.SessionTimeoutMinutes = minutes;
        return this;
    }

    /// <summary>
    /// Configures the <see cref="JsonSerializerOptions"/> used exclusively by the dashboard API
    /// endpoints. These options are intentionally isolated from the job request serialization options
    /// configured via <c>JobsOptionsBuilder.ConfigureRequestJsonOptions</c> to prevent user
    /// customizations from breaking dashboard responses.
    /// </summary>
    /// <param name="configure">Callback to mutate the dashboard <see cref="JsonSerializerOptions"/>.</param>
    public DashboardOptionsBuilder ConfigureDashboardJsonOptions(Action<JsonSerializerOptions>? configure)
    {
        DashboardJsonOptions ??= new JsonSerializerOptions();
        configure?.Invoke(DashboardJsonOptions);
        return this;
    }

    /// <summary>Validate the authentication configuration</summary>
    internal void Validate()
    {
        if (!AuthConfigured)
        {
            throw new InvalidOperationException(
                "Dashboard authentication was not configured. Secure the dashboard with WithBasicAuth, WithApiKey, "
                    + "WithHostAuthentication, or WithCustomAuth, or call WithNoAuth() to explicitly run it "
                    + "unauthenticated (development or trusted-network use only)."
            );
        }

        // Credential completeness for the selected mode is validated once, by the FluentValidation
        // validator wired into the options pipeline (validate-on-start) when these values are copied
        // into AddDashboardAuthentication — not re-checked here.
    }
}
