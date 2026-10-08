// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Imaging;

/// <summary>Chooses libvips, through NetVips, as an imaging provider.</summary>
/// <remarks>
/// The package references only the managed NetVips wrapper. The application supplies libvips: the
/// <c>NetVips.Native</c> package bundles it, or the host installs libvips 8.15 or later. The contributors check this
/// when they are first resolved and throw <see cref="InvalidOperationException" /> naming both fixes.
/// </remarks>
[PublicAPI]
public static class SetupNetVips
{
    extension(HeadlessImagingSetupBuilder setup)
    {
        /// <summary>
        /// Adds the libvips resize and compress contributors with the default <see cref="NetVipsOptions" />.
        /// </summary>
        /// <returns>The same builder so calls can be chained.</returns>
        public HeadlessImagingSetupBuilder UseNetVips()
        {
            setup.RegisterExtension(new NetVipsOptionsExtension(static _ => { }));

            return setup;
        }

        /// <summary>
        /// Adds the libvips resize and compress contributors, binding <see cref="NetVipsOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section that binds <see cref="NetVipsOptions" />.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessImagingSetupBuilder UseNetVips(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(
                new NetVipsOptionsExtension(services => services.Configure<NetVipsOptions>(configuration))
            );

            return setup;
        }

        /// <summary>
        /// Adds the libvips resize and compress contributors, configuring <see cref="NetVipsOptions" /> with a delegate.
        /// </summary>
        /// <param name="configure">Configures <see cref="NetVipsOptions" />.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessImagingSetupBuilder UseNetVips(Action<NetVipsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new NetVipsOptionsExtension(services => services.Configure(configure)));

            return setup;
        }

        /// <summary>
        /// Adds the libvips resize and compress contributors, configuring <see cref="NetVipsOptions" /> with a delegate
        /// that can resolve services.
        /// </summary>
        /// <param name="configure">Configures <see cref="NetVipsOptions" /> using resolved services.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessImagingSetupBuilder UseNetVips(Action<NetVipsOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new NetVipsOptionsExtension(services => services.Configure(configure)));

            return setup;
        }
    }

    private sealed class NetVipsOptionsExtension(Action<IServiceCollection> configure)
        : IImagingProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<NetVipsOptions, NetVipsOptionsValidator>();
            configure(services);

            // Enumerable registrations keyed on the implementation type, so a repeated UseNetVips call adds no second
            // copy of either contributor to the chain.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IImageResizerContributor, NetVipsImageResizerContributor>()
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IImageCompressorContributor, NetVipsImageCompressorContributor>()
            );
        }
    }
}
