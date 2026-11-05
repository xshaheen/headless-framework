// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>Registers the Headless dashboard and mounts it on the host's endpoints.</summary>
[PublicAPI]
public static class SetupDashboard
{
    /// <summary>
    /// The shell's authentication name: the name of its <see cref="AuthConfig"/> options instance, its keyed
    /// <see cref="IAuthService"/>, and its authentication scheme and policies.
    /// </summary>
    internal const string AuthenticationName = "Headless.Dashboard";

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Headless dashboard: one shell that serves every module added inside
        /// <paramref name="configure"/> under one base path, one sign-in, and one session.
        /// </summary>
        /// <remarks>
        /// Mount it with <see cref="MapHeadlessDashboard"/>; a host that registers the dashboard without mapping it
        /// fails to start. Startup reads only the modules' embedded manifests and fails when a contribution override
        /// names an id no module declares.
        /// </remarks>
        /// <param name="configure">
        /// Chooses the authentication mode, which is required, sets the base path and other shell settings, and adds
        /// modules through their <c>Add{Module}</c> extensions.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="configure"/> chose no authentication mode, or the dashboard is already registered.
        /// </exception>
        public IServiceCollection AddHeadlessDashboard(Action<HeadlessDashboardSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            if (services.Any(static descriptor => descriptor.ServiceType == typeof(DashboardRegistration)))
            {
                throw new InvalidOperationException(
                    "AddHeadlessDashboard(...) was already called for this host. Register every module in one call."
                );
            }

            var setup = new HeadlessDashboardSetupBuilder(services);
            configure(setup);
            setup.Validate();

            var registration = new DashboardRegistration([.. setup.Modules]);
            services.AddSingleton(registration);

            // The builder has materialized the shell's AuthConfig; copy it into the shell's named options so the shared
            // name-keyed sign-in code serves the shell like any other dashboard.
            services.AddDashboardAuthentication(
                AuthenticationName,
                auth =>
                {
                    auth.Mode = setup.Auth.Mode;
                    auth.BasicCredentials = setup.Auth.BasicCredentials;
                    auth.ApiKey = setup.Auth.ApiKey;
                    auth.Host = setup.Auth.Host;
                }
            );

            services.Configure<HeadlessDashboardOptions, HeadlessDashboardOptionsValidator>(setup.ApplyTo);

            services.TryAddSingleton<DashboardModuleCatalog>();
            services.TryAddSingleton<DashboardManifestProvider>();
            services.AddStartupValidator<ContributionOverrideValidator>();
            services.AddTransient<IStartupFilter>(_ => new DashboardStartupFilter(registration));

            return services;
        }
    }

    /// <summary>
    /// Mounts the dashboard registered by <c>AddHeadlessDashboard(...)</c> on the host's endpoint routing, under its
    /// base path. This is the only way the dashboard is served: a host that registers it and never calls this method
    /// fails to start.
    /// </summary>
    /// <remarks>
    /// The <c>Headless</c> prefix marks the Headless-owned dashboard, as <c>AddHeadlessDashboard</c> does.
    /// Host-specific conventions for every dashboard endpoint, such as rate limiting, go on the returned builder.
    /// </remarks>
    /// <param name="endpoints">The host's endpoint route builder, such as the <c>WebApplication</c>.</param>
    /// <returns>A builder for conventions that apply to every dashboard endpoint.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The dashboard is not registered through <c>AddHeadlessDashboard(...)</c>, is already mapped, or its base path is
    /// the root.
    /// </exception>
    public static IEndpointConventionBuilder MapHeadlessDashboard(this IEndpointRouteBuilder endpoints)
    {
        Argument.IsNotNull(endpoints);

        var registration =
            endpoints.ServiceProvider.GetService<DashboardRegistration>()
            ?? throw new InvalidOperationException(
                "The Headless dashboard is not registered. Call services.AddHeadlessDashboard(...) before "
                    + "MapHeadlessDashboard()."
            );

        if (registration.Group is not null)
        {
            throw new InvalidOperationException("MapHeadlessDashboard() was already called for this host.");
        }

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<HeadlessDashboardOptions>>().Value;
        var basePath = DashboardSpaHelper.NormalizeMountBasePath(options.BasePath, nameof(MapHeadlessDashboard));

        var group = endpoints.MapGroup(basePath);
        registration.Group = group;

        foreach (var mapEndpoints in registration.EndpointMappers)
        {
            mapEndpoints(group);
        }

        return group;
    }
}

/// <summary>
/// The dashboard's registration: its modules, and the route group <c>MapHeadlessDashboard()</c> created. The shell's
/// own endpoints are added through <see cref="EndpointMappers"/>, which the map call runs on that group.
/// </summary>
internal sealed class DashboardRegistration(IReadOnlyList<DashboardModuleDescriptor> modules)
{
    public IReadOnlyList<DashboardModuleDescriptor> Modules { get; } = modules;

    /// <summary>The shell's endpoint mappers, run in order on the dashboard's route group when it is mapped.</summary>
    public List<Action<RouteGroupBuilder>> EndpointMappers { get; } = [];

    /// <summary>The dashboard's route group at its base path; <see langword="null"/> until the dashboard is mapped.</summary>
    public RouteGroupBuilder? Group { get; set; }
}

/// <summary>
/// Fails the host's start when the dashboard is registered but never mapped, so a missing
/// <c>MapHeadlessDashboard()</c> call is not a dashboard that silently answers 404.
/// </summary>
internal sealed class DashboardStartupFilter(DashboardRegistration registration) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            // The host has mapped its endpoints by now: WebApplication maps them before it builds the pipeline, and a
            // Startup class maps them inside next(app).
            if (registration.Group is null)
            {
                throw new InvalidOperationException(
                    "The Headless dashboard is registered through AddHeadlessDashboard(...) but not mapped. Call "
                        + "app.MapHeadlessDashboard() on the host's endpoints."
                );
            }
        };
    }
}
