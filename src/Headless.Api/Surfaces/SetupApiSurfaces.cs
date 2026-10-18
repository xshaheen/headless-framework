// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        Action<ApiSurfacesBuilder> configure
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        ApiSurfaceRegistration.EnsureOpen(services);
        var builder = new ApiSurfacesBuilder();
        configure(builder);
        var validation = new ApiSurfacesBuilderValidator().Validate(builder);
        if (!validation.IsValid)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(ApiSurfacesBuilder),
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

        services.TryAddSingleton<ApiSurfaceRegistry>();

        return services;
    }

    /// <summary>
    /// Returns the OpenAPI document names of the API surfaces registered so far and closes surface registration, so a
    /// document provider can register one document per surface while the service collection is still writable.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The document name of each registered surface, in registration order.</returns>
    /// <remarks>
    /// A later <see cref="AddHeadlessApiSurface"/> or <see cref="AddHeadlessApiSurfaces"/> call throws, so a surface
    /// registered after the inference cannot silently miss its document. Repeated calls return the same names.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> InferHeadlessApiSurfaceDocumentNames(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        return ApiSurfaceRegistration.InferDocumentNames(services);
    }
}
