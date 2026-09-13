// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Options;
using Headless.Checks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api;

public static class SetupMvcSurfaces
{
    /// <summary>
    /// Adds MVC conventions for API surfaces, enabling <see cref="Mvc.Surfaces.ApiSurfaceAttribute"/> to automatically
    /// apply route prefixes, authorization policies, and surface metadata.
    /// </summary>
    public static IServiceCollection AddHeadlessMvcApiSurfaces(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, ConfigureMvcApiSurfacesOptions>()
        );

        return services;
    }
}
