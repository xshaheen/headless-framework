// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Dashboard.Authentication;
using Headless.Jobs.Coordination;
using Headless.Jobs.Infrastructure.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

public static class SetupJobsDashboard
{
    /// <summary>
    /// Registers the Jobs dashboard: an embedded SPA served at a configurable base path with a
    /// SignalR hub for real-time updates and a configurable authentication layer.
    /// </summary>
    /// <remarks>
    /// The dashboard is auto-injected into the ASP.NET Core middleware pipeline via an
    /// <c>IStartupFilter</c>; no manual <c>app.Use…</c> call is required. To secure the dashboard,
    /// use <see cref="DashboardOptionsBuilder.WithBasicAuth"/>, <see cref="DashboardOptionsBuilder.WithApiKey"/>,
    /// <see cref="DashboardOptionsBuilder.WithHostAuthentication"/>, or
    /// <see cref="DashboardOptionsBuilder.WithCustomAuth"/>. When coordination-based node membership
    /// is registered, a live-nodes bridge is started automatically; otherwise the dashboard stays
    /// inert on the live-nodes panel.
    /// </remarks>
    /// <param name="jobsConfiguration">The jobs options builder.</param>
    /// <param name="configureDashboard">
    /// Callback to configure the dashboard. Authentication must be configured explicitly — via
    /// <see cref="DashboardOptionsBuilder.WithBasicAuth"/>, <see cref="DashboardOptionsBuilder.WithApiKey"/>,
    /// <see cref="DashboardOptionsBuilder.WithHostAuthentication"/>, <see cref="DashboardOptionsBuilder.WithCustomAuth"/>,
    /// or explicitly opted out with <see cref="DashboardOptionsBuilder.WithNoAuth"/> — otherwise the host fails
    /// to start. The dashboard is served at <c>/jobs/dashboard</c> with no CORS policy (same-origin only) by default.
    /// </param>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddDashboard<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        Action<DashboardOptionsBuilder>? configureDashboard = null
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        // No CORS policy by default: the dashboard SPA is served from the same origin as its API, so no
        // cross-origin access is required. Consumers serving the SPA cross-origin opt in explicitly via
        // DashboardOptionsBuilder.SetCorsOrigins / SetCorsPolicy.
        var dashboardConfig = new DashboardOptionsBuilder();

        configureDashboard?.Invoke(dashboardConfig);

        jobsConfiguration.DashboardServiceAction = (services, requestSerializationOptions) =>
        {
            services.AddScoped<
                IJobsDashboardRepository<TTimeJob, TCronJob>,
                JobsDashboardRepository<TTimeJob, TCronJob>
            >();

            services.Replace(
                ServiceDescriptor.Singleton(
                    services.AddSingleton<IJobsNotificationHubSender, JobsNotificationHubSender>()
                )
            );

            // Validate configuration
            dashboardConfig.Validate();

            // Register authentication (named AuthConfig + keyed IAuthService) through the shared
            // Headless.Dashboard.Authentication extension under this dashboard's own name, so another dashboard in
            // the host keeps its own mode. The builder has already materialized the AuthConfig from WithBasicAuth /
            // WithApiKey / WithHostAuthentication / WithCustomAuth, so mirror its fields into the options instance.
            services.AddDashboardAuthentication(
                DashboardOptionsBuilder.AuthenticationName,
                auth =>
                {
                    auth.Mode = dashboardConfig.Auth.Mode;
                    auth.BasicCredentials = dashboardConfig.Auth.BasicCredentials;
                    auth.ApiKey = dashboardConfig.Auth.ApiKey;
                    auth.CustomValidator = dashboardConfig.Auth.CustomValidator;
                    auth.SessionTimeoutMinutes = dashboardConfig.Auth.SessionTimeoutMinutes;
                    auth.HostAuthorizationPolicy = dashboardConfig.Auth.HostAuthorizationPolicy;
                }
            );

            // Add authentication services if using host authentication
            if (dashboardConfig.Auth.Mode == AuthMode.Host)
            {
                var hasAuthenticationService = services.Any(s =>
                    s.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService)
                    || string.Equals(s.ServiceType.Name, "IAuthenticationSchemeProvider", StringComparison.Ordinal)
                );

                if (!hasAuthenticationService)
                {
                    services.AddAuthentication();
                    services.AddAuthorization();
                }
            }

            services.AddDashboardService<TTimeJob, TCronJob>(dashboardConfig, requestSerializationOptions);
            services.AddSingleton<DashboardOptionsBuilder>(_ => dashboardConfig);

            // Live-nodes bridge: pushes membership deltas to the hub. Resolved lazily so the in-memory /
            // no-coordination dashboard path (no INodeMembership registered) still builds — the bridge falls
            // back to NullNodeMembership and stays inert.
            services.AddHostedService(sp => new MembershipDashboardBridge(
                sp.GetService<INodeMembership>() ?? new NullNodeMembership(),
                sp.GetRequiredService<IJobsNotificationHubSender>(),
                sp.GetRequiredService<ILogger<MembershipDashboardBridge>>()
            ));

            // Auto-inject dashboard middleware pipeline via IStartupFilter
            services.AddTransient<IStartupFilter>(_ => new JobsDashboardStartupFilter<TTimeJob, TCronJob>(
                dashboardConfig
            ));

            // A browser cannot put a header on a WebSocket, so under host auth the hub's credential arrives as
            // access_token. It must become the Authorization header before the host's own authentication runs,
            // which is ahead of the dashboard branch, so this filter prepends to the whole pipeline.
            if (dashboardConfig.Auth.Mode == AuthMode.Host)
            {
                services.AddTransient<IStartupFilter>(_ => new HubAccessTokenStartupFilter(dashboardConfig.BasePath));
            }
        };

        return jobsConfiguration;
    }
}

/// <summary>
/// Under host authentication, copies the Jobs hub's <c>access_token</c> query parameter into the
/// <c>Authorization</c> header, ahead of the host's authentication middleware.
/// </summary>
/// <remarks>
/// The dashboard SPA sends the host access key as the <c>Authorization</c> header on API calls and as
/// <c>access_token</c> on the hub, because a browser cannot set headers on a WebSocket. Only requests to the hub and
/// its negotiate endpoint that carry no <c>Authorization</c> header are changed. The host's scheme then signs the
/// request in as it would an API call, and the hub's authorization policy evaluates that user. Cookie sign-in needs
/// none of this.
/// </remarks>
internal sealed class HubAccessTokenStartupFilter(string basePath) : IStartupFilter
{
    private readonly string _hubPath =
        DashboardSpaHelper.NormalizeBasePath(basePath).TrimEnd('/') + JobsNotificationHub.Path;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(
                (context, nextMiddleware) =>
                {
                    if (
                        string.IsNullOrEmpty(context.Request.Headers.Authorization)
                        && _IsHubPath(context.Request.Path.Value)
                        && context.Request.Query["access_token"].FirstOrDefault() is { Length: > 0 } accessToken
                        && !_HasControlCharacter(accessToken)
                    )
                    {
                        context.Request.Headers.Authorization = accessToken;
                    }

                    return nextMiddleware(context);
                }
            );

            next(app);
        };
    }

    // A header value never carries control characters, so a token with any (such as an encoded CR/LF) is left out
    // rather than handed to the host's handlers and anything that logs or forwards request headers.
    private static bool _HasControlCharacter(string value)
    {
        return value.AsSpan().ContainsAnyInRange('\u0000', '\u001f')
            || value.Contains('\u007f', StringComparison.Ordinal);
    }

    // The host may add a path base later in its pipeline, so match the end of the path, not its start.
    private bool _IsHubPath(string? path)
    {
        return path is not null
            && (
                path.EndsWith(_hubPath, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(_hubPath + "/negotiate", StringComparison.OrdinalIgnoreCase)
            );
    }
}

internal sealed class JobsDashboardStartupFilter<TTimeJob, TCronJob>(DashboardOptionsBuilder config) : IStartupFilter
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);
            app.UseDashboardWithEndpoints<TTimeJob, TCronJob>(config);
        };
    }
}
