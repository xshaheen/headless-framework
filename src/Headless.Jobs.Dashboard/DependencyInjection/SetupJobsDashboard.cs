// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Dashboard.Authentication;
using Headless.Jobs.Coordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Hubs;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.DependencyInjection;

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
    /// Optional callback to configure the dashboard. When <see langword="null"/>, the dashboard is
    /// served at <c>/jobs/dashboard</c> with CORS open to all origins and no authentication.
    /// </param>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddDashboard<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        Action<DashboardOptionsBuilder>? configureDashboard = null
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var dashboardConfig = new DashboardOptionsBuilder
        {
            CorsPolicyBuilder = cors => cors.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowCredentials(),
        };

        configureDashboard?.Invoke(dashboardConfig);

        jobsConfiguration.DashboardServiceAction = (services) =>
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

            // Register authentication (AuthConfig + IAuthService) through the shared
            // Headless.Dashboard.Authentication extension. The builder has already materialized the AuthConfig
            // from WithBasicAuth / WithApiKey / WithHostAuthentication / WithCustomAuth, so mirror its fields
            // into the options-bound instance instead of hand-registering the singleton and the auth service.
            services.AddDashboardAuthentication(auth =>
            {
                auth.Mode = dashboardConfig.Auth.Mode;
                auth.BasicCredentials = dashboardConfig.Auth.BasicCredentials;
                auth.ApiKey = dashboardConfig.Auth.ApiKey;
                auth.CustomValidator = dashboardConfig.Auth.CustomValidator;
                auth.SessionTimeoutMinutes = dashboardConfig.Auth.SessionTimeoutMinutes;
                auth.HostAuthorizationPolicy = dashboardConfig.Auth.HostAuthorizationPolicy;
            });

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

            services.AddDashboardService<TTimeJob, TCronJob>(dashboardConfig);
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
        };

        return jobsConfiguration;
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
