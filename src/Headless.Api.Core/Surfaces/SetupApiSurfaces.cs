// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.Configure<Surfaces.ApiSurfaceOptions, Surfaces.ApiSurfaceOptionsValidator>(configure);
        services.TryAddSingleton<Surfaces.ApiSurfaceRegistry>();

        return services;
    }
}
