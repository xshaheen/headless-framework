// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Imaging.ImageSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Imaging;

/// <summary>Chooses ImageSharp as an imaging provider.</summary>
[PublicAPI]
public static class SetupImageSharp
{
    extension(HeadlessImagingSetupBuilder setup)
    {
        /// <summary>
        /// Adds the ImageSharp resize and compress contributors with the default <see cref="ImageSharpOptions" />.
        /// </summary>
        /// <returns>The same builder so calls can be chained.</returns>
        public HeadlessImagingSetupBuilder UseImageSharp()
        {
            setup.RegisterExtension(new ImageSharpOptionsExtension(static _ => { }));

            return setup;
        }

        /// <summary>
        /// Adds the ImageSharp resize and compress contributors, binding <see cref="ImageSharpOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section that binds <see cref="ImageSharpOptions" />.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessImagingSetupBuilder UseImageSharp(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(
                new ImageSharpOptionsExtension(services => services.Configure<ImageSharpOptions>(configuration))
            );

            return setup;
        }

        /// <summary>
        /// Adds the ImageSharp resize and compress contributors, configuring <see cref="ImageSharpOptions" /> with a
        /// delegate.
        /// </summary>
        /// <param name="configure">Configures <see cref="ImageSharpOptions" />.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessImagingSetupBuilder UseImageSharp(Action<ImageSharpOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new ImageSharpOptionsExtension(services => services.Configure(configure)));

            return setup;
        }

        /// <summary>
        /// Adds the ImageSharp resize and compress contributors, configuring <see cref="ImageSharpOptions" /> with a
        /// delegate that can resolve services.
        /// </summary>
        /// <param name="configure">Configures <see cref="ImageSharpOptions" /> using resolved services.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessImagingSetupBuilder UseImageSharp(Action<ImageSharpOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new ImageSharpOptionsExtension(services => services.Configure(configure)));

            return setup;
        }
    }

    private sealed class ImageSharpOptionsExtension(Action<IServiceCollection> configure)
        : IImagingProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<ImageSharpOptions, ImageSharpOptionsValidator>();
            configure(services);

            // Enumerable registrations keyed on the implementation type, so a repeated UseImageSharp call adds no
            // second copy of either contributor to the chain.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IImageResizerContributor, ImageSharpImageResizerContributor>()
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IImageCompressorContributor, ImageSharpImageCompressorContributor>()
            );
        }
    }
}
