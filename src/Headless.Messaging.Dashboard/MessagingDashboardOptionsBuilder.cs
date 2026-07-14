// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Fluent builder for configuring the Messaging Dashboard.
/// Mirrors Jobs Dashboard's <c>DashboardOptionsBuilder</c> pattern.
/// </summary>
public sealed class MessagingDashboardOptionsBuilder
{
    internal string BasePath { get; set; } = "/messaging";
    internal Action<CorsPolicyBuilder>? CorsPolicyBuilder { get; set; }

    /// <summary>
    /// Authentication configuration (shared with Jobs Dashboard).
    /// </summary>
    internal AuthConfig Auth { get; set; } = new();

    // Tracks whether an authentication mode was explicitly chosen. The dashboard fails closed at startup
    // (see Validate) when this is false, so it cannot ship publicly by omission.
    internal bool AuthConfigured { get; private set; }

    // Custom Middleware Integration

    /// <summary>
    /// Optional middleware injected after authentication but before the Minimal API endpoints.
    /// Useful for adding custom request inspection, logging, or authorization logic scoped to the dashboard.
    /// </summary>
    public Action<IApplicationBuilder>? CustomMiddleware { get; set; }

    /// <summary>
    /// Optional middleware injected at the very start of the dashboard branch, before static files
    /// and authentication. Runs on every request that matches the dashboard base path.
    /// </summary>
    public Action<IApplicationBuilder>? PreDashboardMiddleware { get; set; }

    /// <summary>
    /// Optional middleware injected after all dashboard endpoints and the SPA fallback handler.
    /// </summary>
    public Action<IApplicationBuilder>? PostDashboardMiddleware { get; set; }

    /// <summary>
    /// The interval the /stats endpoint should be polled with (milliseconds). Default: 2000.
    /// </summary>
    internal int StatsPollingInterval { get; set; } = 2000;

    /// <summary>
    /// Set the base path for the dashboard. Default: <c>/messaging</c>.
    /// </summary>
    public MessagingDashboardOptionsBuilder SetBasePath(string basePath)
    {
        BasePath = basePath;
        return this;
    }

    /// <summary>
    /// Configure the CORS policy for the dashboard. By default no CORS policy is applied: the dashboard SPA
    /// is served from the same origin as its API, so no cross-origin access is required. Prefer
    /// <see cref="SetCorsOrigins"/> for the common cross-origin case.
    /// </summary>
    public MessagingDashboardOptionsBuilder SetCorsPolicy(Action<CorsPolicyBuilder> corsPolicyBuilder)
    {
        CorsPolicyBuilder = corsPolicyBuilder;
        return this;
    }

    /// <summary>
    /// Restricts cross-origin access to the given origins with a credentialed policy. Use this when the
    /// dashboard SPA is served from a different origin than its API; when they share an origin (the default)
    /// no CORS configuration is needed.
    /// </summary>
    /// <param name="origins">The exact allowed origins, e.g. <c>https://admin.example.com</c>. Must not be empty.</param>
    public MessagingDashboardOptionsBuilder SetCorsOrigins(params string[] origins)
    {
        Argument.IsNotNullOrEmpty(origins);
        CorsPolicyBuilder = cors => cors.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        return this;
    }

    /// <summary>
    /// Set the polling interval for the stats endpoint. Default: 2000ms.
    /// </summary>
    public MessagingDashboardOptionsBuilder SetStatsPollingInterval(int intervalMs)
    {
        StatsPollingInterval = intervalMs;
        return this;
    }

    // --- Auth fluent API (matches Jobs DashboardOptionsBuilder) ---

    /// <summary>Configure no authentication (public dashboard).</summary>
    public MessagingDashboardOptionsBuilder WithNoAuth()
    {
        Auth.Mode = AuthMode.None;
        AuthConfigured = true;
        return this;
    }

    /// <summary>Enable Basic Authentication with username/password.</summary>
    public MessagingDashboardOptionsBuilder WithBasicAuth(string username, string password)
    {
        Auth.Mode = AuthMode.Basic;
        Auth.BasicCredentials = $"{username}:{password}".ToBase64();
        AuthConfigured = true;
        return this;
    }

    /// <summary>Enable API Key authentication (sent as Bearer token).</summary>
    public MessagingDashboardOptionsBuilder WithApiKey(string apiKey)
    {
        Auth.Mode = AuthMode.ApiKey;
        Auth.ApiKey = apiKey;
        AuthConfigured = true;
        return this;
    }

    /// <summary>Use the host application's existing authentication system.</summary>
    /// <param name="policy">Optional authorization policy name. If null, uses the default policy.</param>
    public MessagingDashboardOptionsBuilder WithHostAuthentication(string? policy = null)
    {
        Auth.Mode = AuthMode.Host;
        Auth.HostAuthorizationPolicy = policy;
        AuthConfigured = true;
        return this;
    }

    /// <summary>Configure custom authentication with validation function.</summary>
    public MessagingDashboardOptionsBuilder WithCustomAuth(Func<string, IServiceProvider, bool> validator)
    {
        Auth.Mode = AuthMode.Custom;
        Auth.CustomValidator = validator;
        AuthConfigured = true;
        return this;
    }

    /// <summary>Set session timeout in minutes.</summary>
    public MessagingDashboardOptionsBuilder WithSessionTimeout(int minutes)
    {
        Auth.SessionTimeoutMinutes = minutes;
        return this;
    }

    /// <summary>Validate the configuration.</summary>
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

        Auth.Validate();
    }
}
