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

    // Custom Middleware Integration
    public Action<IApplicationBuilder>? CustomMiddleware { get; set; }
    public Action<IApplicationBuilder>? PreDashboardMiddleware { get; set; }
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
    /// Configure CORS policy for the dashboard.
    /// </summary>
    public MessagingDashboardOptionsBuilder SetCorsPolicy(Action<CorsPolicyBuilder> corsPolicyBuilder)
    {
        CorsPolicyBuilder = corsPolicyBuilder;
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
        return this;
    }

    /// <summary>Enable Basic Authentication with username/password.</summary>
    public MessagingDashboardOptionsBuilder WithBasicAuth(string username, string password)
    {
        Auth.Mode = AuthMode.Basic;
        Auth.BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return this;
    }

    /// <summary>Enable API Key authentication (sent as Bearer token).</summary>
    public MessagingDashboardOptionsBuilder WithApiKey(string apiKey)
    {
        Auth.Mode = AuthMode.ApiKey;
        Auth.ApiKey = apiKey;
        return this;
    }

    /// <summary>Use the host application's existing authentication system.</summary>
    /// <param name="policy">Optional authorization policy name. If null, uses the default policy.</param>
    public MessagingDashboardOptionsBuilder WithHostAuthentication(string? policy = null)
    {
        Auth.Mode = AuthMode.Host;
        Auth.HostAuthorizationPolicy = policy;
        return this;
    }

    /// <summary>Configure custom authentication with validation function.</summary>
    public MessagingDashboardOptionsBuilder WithCustomAuth(Func<string, IServiceProvider, bool> validator)
    {
        Auth.Mode = AuthMode.Custom;
        Auth.CustomValidator = validator;
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
        Auth.Validate();
    }
}
