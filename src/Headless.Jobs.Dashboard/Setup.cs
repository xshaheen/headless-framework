// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.Dashboard;
using Headless.Jobs.Coordination;
using Headless.Jobs.Infrastructure.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
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
    /// Mount the dashboard on the host's endpoints with <see cref="MapHeadlessJobsDashboard"/>; a host that registers it
    /// without mapping it fails to start. To secure the dashboard,
    /// use <see cref="DashboardOptionsBuilder.WithBasicAuth"/>, <see cref="DashboardOptionsBuilder.WithApiKey"/>, or
    /// <see cref="DashboardOptionsBuilder.WithHostAuthentication"/>. When coordination-based node membership
    /// is registered, a live-nodes bridge is started automatically; otherwise the dashboard stays
    /// inert on the live-nodes panel.
    /// </remarks>
    /// <param name="jobsConfiguration">The jobs options builder.</param>
    /// <param name="configureDashboard">
    /// Callback to configure the dashboard. Authentication must be configured explicitly — via
    /// <see cref="DashboardOptionsBuilder.WithBasicAuth"/>, <see cref="DashboardOptionsBuilder.WithApiKey"/>,
    /// <see cref="DashboardOptionsBuilder.WithHostAuthentication"/>,
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
            // Headless.Dashboard authentication extension under this dashboard's own name, so another dashboard in
            // the host keeps its own mode. The builder has already materialized the AuthConfig from WithBasicAuth /
            // WithApiKey / WithHostAuthentication, so mirror its fields into the options instance.
            services.AddDashboardAuthentication(
                DashboardOptionsBuilder.AuthenticationName,
                auth =>
                {
                    auth.Mode = dashboardConfig.Auth.Mode;
                    auth.BasicCredentials = dashboardConfig.Auth.BasicCredentials;
                    auth.ApiKey = dashboardConfig.Auth.ApiKey;
                    auth.Host = dashboardConfig.Auth.Host;
                }
            );

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

            dashboardConfig.MapEndpoints = group => group.MapDashboard<TTimeJob, TCronJob>(dashboardConfig);
            services.AddTransient<IStartupFilter>(_ => new JobsDashboardStartupFilter(dashboardConfig));
        };

        return jobsConfiguration;
    }

    /// <summary>
    /// Mounts the Jobs dashboard registered by <c>AddDashboard(...)</c> on the host's endpoint routing, under its base
    /// path: the API, the SignalR hub, and the SPA. This is the only way the dashboard is served: a host that
    /// registers it and never calls this method fails to start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host's <c>UseAuthorization</c> evaluates the dashboard's access rule, never the host's fallback policy. The
    /// SPA shell, its static assets, and the <c>/auth/sign-in</c>, <c>/api/auth/info</c>, <c>/api/auth/session</c>,
    /// <c>/api/auth/sign-out</c>, and <c>/api/auth/validate</c> endpoints are anonymous, so a browser loads the shell
    /// and signs in without a credential. Under <see cref="DashboardOptionsBuilder.WithHostAuthentication"/> every other
    /// <c>{basePath}/api</c> endpoint and the hub require the dashboard's session cookie, issued to a host user who
    /// passes the view policy; the API's state-changing endpoints also require the operate policy. Under Basic or API
    /// key authentication they require the dashboard's own credential, checked by the dashboard's authentication
    /// scheme; <see cref="DashboardOptionsBuilder.WithNoAuth"/> leaves them open.
    /// </para>
    /// <para>
    /// Under host authentication the hub also refuses a request whose <c>Origin</c> header names another origin, and
    /// closes an open connection when the session it connected with expires.
    /// </para>
    /// <para>
    /// The host's pipeline must run <c>UseAuthorization</c> between routing and endpoints (a <c>WebApplication</c>
    /// adds it when the host does not), <c>UseAuthentication</c> before it for host authentication, and
    /// <c>UseCors</c> only when <c>SetCorsOrigins</c> or <c>SetCorsPolicy</c> was configured. Host-specific behavior
    /// for the dashboard, such as rate limiting or an extra policy, goes on the returned builder.
    /// </para>
    /// <para>
    /// A host that calls <c>UseAuthentication</c> must also call <c>UseAuthorization</c> after it: the
    /// <c>UseAuthorization</c> a <c>WebApplication</c> adds on its own runs ahead of the host's
    /// <c>UseAuthentication</c>, before host authentication has signed the user in.
    /// </para>
    /// <para>
    /// The <c>Headless</c> prefix marks the Headless-owned dashboard, as <c>AddHeadlessJobs</c> does.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The host's endpoint route builder, such as the <c>WebApplication</c>.</param>
    /// <returns>A builder for conventions that apply to every dashboard endpoint.</returns>
    /// <exception cref="InvalidOperationException">
    /// The dashboard is not registered through <c>AddDashboard(...)</c>, it is already mapped, or its base path is the
    /// root.
    /// </exception>
    public static IEndpointConventionBuilder MapHeadlessJobsDashboard(this IEndpointRouteBuilder endpoints)
    {
        Argument.IsNotNull(endpoints);

        var config = endpoints.ServiceProvider.GetService<DashboardOptionsBuilder>();
        if (config?.MapEndpoints is not { } mapEndpoints)
        {
            throw new InvalidOperationException(
                "The Jobs dashboard is not registered. Call AddDashboard(...) inside AddHeadlessJobs(...) before "
                    + "MapHeadlessJobsDashboard()."
            );
        }

        var basePath = DashboardSpaHelper.NormalizeMountBasePath(config.BasePath, nameof(MapHeadlessJobsDashboard));

        if (config.EndpointMounted)
        {
            throw new InvalidOperationException("MapHeadlessJobsDashboard() was already called for this host.");
        }

        config.EndpointMounted = true;

        var group = endpoints.MapGroup(basePath);
        mapEndpoints(group);

        return group;
    }
}

/// <summary>
/// Fails the host's start when the dashboard is registered but never mapped, so a missing
/// <c>MapHeadlessJobsDashboard()</c> call is not a dashboard that silently answers 404.
/// </summary>
internal sealed class JobsDashboardStartupFilter(DashboardOptionsBuilder config) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            // The host has mapped its endpoints by now: WebApplication maps them before it builds the pipeline, and a
            // Startup class maps them inside next(app).
            if (!config.EndpointMounted)
            {
                throw new InvalidOperationException(
                    "The Jobs dashboard is registered through AddDashboard(...) but not mapped. Call "
                        + "app.MapHeadlessJobsDashboard() on the host's endpoints."
                );
            }
        };
    }
}
