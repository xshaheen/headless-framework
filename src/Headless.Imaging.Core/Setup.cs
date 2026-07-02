// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Imaging;

[PublicAPI]
public static class SetupCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the core imaging services and binds <see cref="ImagingOptions"/> from
        /// <paramref name="config"/>.
        /// </summary>
        /// <param name="config">
        /// Configuration section whose keys map to <see cref="ImagingOptions"/> properties.
        /// </param>
        /// <returns>
        /// An <see cref="AddImagingBuilder"/> for chaining provider registrations such as
        /// <c>AddImageSharpContributors</c>.
        /// </returns>
        public AddImagingBuilder AddImaging(IConfiguration config)
        {
            services.Configure<ImagingOptions, ImagingOptionsValidator>(config);

            return _AddImagingCore(services);
        }

        /// <summary>
        /// Adds the core imaging services and optionally configures <see cref="ImagingOptions"/>
        /// via <paramref name="setupAction"/>.
        /// </summary>
        /// <param name="setupAction">
        /// Optional delegate to configure <see cref="ImagingOptions"/>. When <see langword="null"/>,
        /// default option values are used.
        /// </param>
        /// <returns>
        /// An <see cref="AddImagingBuilder"/> for chaining provider registrations.
        /// </returns>
        public AddImagingBuilder AddImaging(Action<ImagingOptions>? setupAction = null)
        {
            services.Configure<ImagingOptions, ImagingOptionsValidator>(setupAction);

            return _AddImagingCore(services);
        }

        /// <summary>
        /// Adds the core imaging services and configures <see cref="ImagingOptions"/> via a
        /// factory delegate that receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="setupAction">
        /// Delegate that configures <see cref="ImagingOptions"/> using the resolved service provider.
        /// </param>
        /// <returns>
        /// An <see cref="AddImagingBuilder"/> for chaining provider registrations.
        /// </returns>
        public AddImagingBuilder AddImaging(Action<ImagingOptions, IServiceProvider> setupAction)
        {
            services.Configure<ImagingOptions, ImagingOptionsValidator>(setupAction);

            return _AddImagingCore(services);
        }
    }

    private static AddImagingBuilder _AddImagingCore(IServiceCollection services)
    {
        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddSingleton<IImageCompressor, ImageCompressor>();

        return new(services);
    }
}
