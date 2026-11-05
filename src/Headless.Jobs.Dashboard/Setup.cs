// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.Jobs.Coordination;
using Headless.Jobs.Infrastructure.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Jobs;

public static class SetupJobsDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Jobs module to the unified dashboard under <see cref="JobsDashboardOptions.ModuleId"/>: runs, cron
        /// jobs, functions, and nodes of the store <c>AddHeadlessJobs</c> configured, with Needs you, Overview figures,
        /// live changes from every node, commands, and Operations.
        /// </summary>
        /// <remarks>
        /// The module reads the entity types <c>AddHeadlessJobs&lt;TTimeJob, TCronJob&gt;</c> registered, so it takes
        /// none. A host that adds it without calling <c>AddHeadlessJobs</c> fails to start with a message naming that
        /// call.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>jobs</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddJobs()
        {
            return dashboard.AddJobs(static _ => { });
        }

        /// <summary>Adds the Jobs module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's policies and change reads.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>jobs</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddJobs(Action<JobsDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<JobsDashboardOptions, JobsDashboardOptionsValidator>(setupAction);

            return _AddJobsModule(dashboard);
        }

        /// <summary>Adds the Jobs module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies and change reads.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>jobs</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddJobs(Action<JobsDashboardOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<JobsDashboardOptions, JobsDashboardOptionsValidator>(setupAction);

            return _AddJobsModule(dashboard);
        }

        /// <summary>
        /// Adds the Jobs module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c>,
        /// <c>OperatePolicy</c>, <c>ChangeOverlapWindow</c>, and <c>ChangePageSize</c>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>jobs</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddJobs(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<JobsDashboardOptions, JobsDashboardOptionsValidator>(configuration);

            return _AddJobsModule(dashboard);
        }
    }

    private static HeadlessDashboardSetupBuilder _AddJobsModule(HeadlessDashboardSetupBuilder dashboard)
    {
        var services = dashboard.Services;

        // The module reaches Jobs through the seam AddHeadlessJobs registers; without it nothing could serve a request,
        // so the host fails at start rather than on the first dashboard call.
        services.RequireRegisteredService<IJobsDashboardBackend>(
            "the Jobs dashboard module (AddJobs)",
            "Call services.AddHeadlessJobs(...) to register Headless Jobs."
        );

        services.TryAddSingleton(provider => new JobsChangePublisher(
            _Backend(provider),
            provider.GetRequiredService<IDashboardEventPublisher>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<JobsChangePublisher>>()
        ));

        // Replace, not TryAdd: AddHeadlessJobs may have registered its no-op sender first; when it runs after this
        // call, its TryAdd keeps this one.
        services.Replace(ServiceDescriptor.Singleton<IJobsNotificationHubSender, JobsDashboardEventNotifier>());

        services.TryAddSingleton(provider => new JobsModule(
            _Backend(provider),
            provider.GetRequiredService<JobsChangePublisher>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<JobsDashboardOptions>>().Value
        ));

        return dashboard.AddModule<JobsModule, JobsDashboardOptions>(JobsDashboardOptions.ModuleId);
    }

    private const string _MissingJobs =
        "The Jobs dashboard module (AddJobs) needs Headless Jobs. Call services.AddHeadlessJobs(...) to register it.";

    // The shell may build the module while it maps endpoints, before startup validation reports the missing call, so
    // the module names it too.
    private static IJobsDashboardBackend _Backend(IServiceProvider provider) =>
        provider.GetService<IJobsDashboardBackend>() ?? throw new InvalidOperationException(_MissingJobs);

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
