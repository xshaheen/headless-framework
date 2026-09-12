// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api;

public static class SetupApiSurfaces
{
    /// <summary>
    /// Registers and configures Headless API surfaces options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration delegate for setting up API surfaces.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHeadlessApiSurfaces(
        this IServiceCollection services,
        Action<Surfaces.ApiSurfaceOptions> configure
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        services.Configure(configure);

        return services;
    }

    /// <summary>
    /// Adds <see cref="Surfaces.ApiSurfaceMiddleware"/> to the HTTP request pipeline.
    /// Should be placed immediately after <c>UseRouting()</c> and before authentication or authorization.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseHeadlessApiSurfaces(this IApplicationBuilder app)
    {
        Argument.IsNotNull(app);

        return app.UseMiddleware<Surfaces.ApiSurfaceMiddleware>();
    }
}
