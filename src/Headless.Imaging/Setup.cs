// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Imaging;

/// <summary>Registers the imaging pipeline and its providers.</summary>
[PublicAPI]
public static class SetupImaging
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IImageResizer" />, <see cref="IImageCompressor" />, and the providers chosen in
        /// <paramref name="configure" />. At least one <c>Use…</c> call, such as <c>UseImageSharp</c> from
        /// <c>Headless.Imaging.ImageSharp</c>, is required.
        /// </summary>
        /// <param name="configure">Binds <see cref="ImagingOptions" /> and chooses the providers.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No provider was chosen, or imaging was already registered on this service collection.
        /// </exception>
        public IServiceCollection AddHeadlessImaging(Action<HeadlessImagingSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessImagingSetupBuilder(services);
            configure(setup);

            return _AddImagingCore(services, setup);
        }
    }

    private static IServiceCollection _AddImagingCore(IServiceCollection services, HeadlessImagingSetupBuilder setup)
    {
        // Without a contributor every resize and compress call reports NotSupported, which is a wiring mistake
        // that would otherwise surface only on the first image a user uploads.
        if (setup.Extensions.Count == 0)
        {
            throw new InvalidOperationException(
                "Headless.Imaging requires at least one provider. Call `UseImageSharp` (Headless.Imaging.ImageSharp)."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(ImagingRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessImaging was already called on this service collection. Configure imaging and all its "
                    + "providers in a single AddHeadlessImaging call."
            );
        }

        services.AddSingleton(new ImagingRegistration());
        services.AddOptions<ImagingOptions, ImagingOptionsValidator>();
        services.TryAddSingleton<IImageResizer, ImageResizer>();
        services.TryAddSingleton<IImageCompressor, ImageCompressor>();

        foreach (var extension in setup.Extensions)
        {
            extension.AddServices(services);
        }

        return services;
    }

    private sealed record ImagingRegistration;
}
