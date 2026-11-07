// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Fluent builder for configuring the Messaging Dashboard.
/// Mirrors Jobs Dashboard's <c>DashboardOptionsBuilder</c> pattern.
/// </summary>
public sealed class MessagingDashboardOptionsBuilder
{
    /// <summary>
    /// The Messaging dashboard's authentication name: the name of its <see cref="AuthConfig"/> options instance and
    /// the key of its scoped <see cref="IAuthService"/>. Resolve or replace this dashboard's auth service by this key;
    /// it is independent of any other dashboard's authentication in the same host.
    /// </summary>
    public const string AuthenticationName = "Headless.Messaging.Dashboard";

    internal string BasePath { get; set; } = "/messaging/dashboard";
    internal Action<CorsPolicyBuilder>? CorsPolicyBuilder { get; set; }

    /// <summary>
    /// Authentication configuration, registered under <see cref="AuthenticationName"/>.
    /// </summary>
    internal AuthConfig Auth { get; set; } = new();

    // Tracks whether an authentication mode was explicitly chosen. The dashboard fails closed at startup
    // (see Validate) when this is false, so it cannot ship publicly by omission.
    internal bool AuthConfigured { get; private set; }

    // Set by MapHeadlessMessagingDashboard. The startup filter reads it when the host pipeline is built, after the
    // host has mapped its endpoints, and fails the start when it is still false.
    internal bool EndpointMounted { get; set; }

    /// <summary>
    /// The interval the /stats endpoint should be polled with (milliseconds). Default: 2000.
    /// </summary>
    internal int StatsPollingInterval { get; set; } = 2000;

    /// <summary>
    /// Set the base path for the dashboard. Default: <c>/messaging/dashboard</c>.
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

    /// <summary>
    /// Signs host users into the dashboard with its own session cookie, issued from the host application's sign-in.
    /// </summary>
    /// <remarks>
    /// The dashboard must be served from the host's own origin: host mode cannot be combined with
    /// <see cref="SetCorsOrigins"/> or <see cref="SetCorsPolicy"/>, and the host fails to start when it is. The gateway
    /// proxy does not forward host-mode requests to other nodes; each node serves its own data.
    /// </remarks>
    /// <param name="configure">
    /// Configures the view and operate policies, the sign-in challenge scheme, and the session's cookie and lifetime.
    /// When <see langword="null"/>, the host's default policy decides both viewing and operating.
    /// </param>
    public MessagingDashboardOptionsBuilder WithHostAuthentication(
        Action<DashboardHostAuthenticationOptions>? configure = null
    )
    {
        var host = new DashboardHostAuthenticationOptions();
        configure?.Invoke(host);

        Auth.Mode = AuthMode.Host;
        Auth.Host = host;
        AuthConfigured = true;
        return this;
    }

    /// <summary>Validate the configuration.</summary>
    internal void Validate()
    {
        if (!AuthConfigured)
        {
            throw new InvalidOperationException(
                "Dashboard authentication was not configured. Secure the dashboard with WithBasicAuth, WithApiKey, "
                    + "or WithHostAuthentication, or call WithNoAuth() to explicitly run it "
                    + "unauthenticated (development or trusted-network use only)."
            );
        }

        // Credential completeness for the selected mode is validated once, by the FluentValidation
        // validator wired into the options pipeline (validate-on-start) when these values are copied
        // into AddDashboardAuthentication — not re-checked here.
    }
}
