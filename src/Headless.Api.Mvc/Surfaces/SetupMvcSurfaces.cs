// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Options;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api;

public static class SetupMvcSurfaces
{
    /// <summary>
    /// Adds MVC conventions for API surfaces, enabling <see cref="Mvc.Surfaces.ApiSurfaceAttribute"/> to automatically
    /// apply route prefixes, authorization policies, and OpenAPI group names.
    /// </summary>
    public static IServiceCollection AddHeadlessMvcApiSurfaces(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        services.ConfigureOptions<ConfigureMvcApiSurfacesOptions>();

        return services;
    }
}
