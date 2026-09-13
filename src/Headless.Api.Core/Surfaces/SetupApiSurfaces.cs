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
    /// <summary>Configures, validates, and freezes one API surface during service registration.</summary>
    /// <remarks>Register every surface before AddHeadless or inferred surface document registration.</remarks>
    /// <exception cref="ArgumentException">The surface name is empty.</exception>
    /// <exception cref="OptionsValidationException">The surface definition is invalid.</exception>
    /// <exception cref="InvalidOperationException">An identity is already registered or document inference has closed registration.</exception>
    public static IServiceCollection AddHeadlessApiSurface(
        this IServiceCollection services,
        string surfaceName,
        Action<ApiSurfaceBuilder>? configure = null
    ) => services.AddHeadlessApiSurfaces(surfaces => surfaces.Add(surfaceName, configure));

    /// <summary>
    /// Configures, validates, and freezes a batch of API surfaces atomically during service registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration delegate for setting up API surfaces.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="OptionsValidationException">A surface definition is invalid.</exception>
    /// <exception cref="InvalidOperationException">An identity is duplicated or document inference has closed registration.</exception>
    public static IServiceCollection AddHeadlessApiSurfaces(
        this IServiceCollection services,
        Action<Surfaces.ApiSurfacesBuilder> configure
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        ApiSurfaceRegistration.EnsureOpen(services);
        var builder = new Surfaces.ApiSurfacesBuilder();
        configure(builder);
        var validation = new ApiSurfacesBuilderValidator().Validate(builder);
        if (!validation.IsValid)
        {
            throw new OptionsValidationException(
                Microsoft.Extensions.Options.Options.DefaultName,
                typeof(Surfaces.ApiSurfacesBuilder),
                validation.Errors.Select(error => error.ErrorMessage)
            );
        }

        var surfaces = builder.Surfaces.Select(surface => surface.Build()).ToArray();
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
