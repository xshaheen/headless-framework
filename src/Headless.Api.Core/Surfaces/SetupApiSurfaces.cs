// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api;

public static class SetupApiSurfaces
{
    /// <summary>
    /// Configures, validates, and freezes API surface definitions during service registration.
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

        ApiSurfaceRegistration.EnsureOpen(services);
        var options = new Surfaces.ApiSurfaceOptions();
        configure(options);
        var validation = new ApiSurfaceOptionsValidator().Validate(options);
        if (!validation.IsValid)
        {
            throw new OptionsValidationException(
                Microsoft.Extensions.Options.Options.DefaultName,
                typeof(Surfaces.ApiSurfaceOptions),
                validation.Errors.Select(error => error.ErrorMessage)
            );
        }

        var surfaces = options.Surfaces.Select(surface => surface.Build()).ToArray();
        var existing = ApiSurfaceRegistration.GetSurfaces(services);
        foreach (var surface in surfaces)
        {
            if (
                existing.Any(value =>
                    string.Equals(value.SurfaceName, surface.SurfaceName, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                throw new InvalidOperationException($"API surface '{surface.SurfaceName}' is already configured.");
            }
            if (
                existing.Any(value =>
                    string.Equals(
                        value.OpenApi.DocumentName,
                        surface.OpenApi.DocumentName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                throw new InvalidOperationException(
                    $"API surface document '{surface.OpenApi.DocumentName}' is already configured."
                );
            }
        }
        foreach (var surface in surfaces)
        {
            services.AddSingleton(surface);
        }
        services.TryAddSingleton<Surfaces.ApiSurfaceRegistry>();

        return services;
    }
}
