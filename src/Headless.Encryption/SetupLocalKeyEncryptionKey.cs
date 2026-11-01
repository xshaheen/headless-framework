// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Encryption;

/// <summary>Chooses the local key-encryption key, held by the host, as the encryption key-encryption key.</summary>
[PublicAPI]
public static class SetupLocalKeyEncryptionKey
{
    extension(HeadlessEncryptionSetupBuilder setup)
    {
        /// <summary>
        /// Wraps data keys with a local key, binding <see cref="LocalKeyEncryptionKeyOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// Whoever reads the configured keys can read every encrypted value, and a database backup is protected only
        /// while the keys stay out of it. Prefer a key service in production when one is available.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseLocalKey(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterKeyEncryptionKeyProvider(new LocalKeyOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Wraps data keys with a local key, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>Whoever reads the configured keys can read every encrypted value.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseLocalKey(Action<LocalKeyEncryptionKeyOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterKeyEncryptionKeyProvider(new LocalKeyOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Wraps data keys with a local key, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>Whoever reads the configured keys can read every encrypted value.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseLocalKey(
            Action<LocalKeyEncryptionKeyOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterKeyEncryptionKeyProvider(new LocalKeyOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class LocalKeyOptionsExtension : IEncryptionProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<LocalKeyEncryptionKeyOptions>? _configure;
        private readonly Action<LocalKeyEncryptionKeyOptions, IServiceProvider>? _configureWithServices;

        public LocalKeyOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public LocalKeyOptionsExtension(Action<LocalKeyEncryptionKeyOptions> configure)
        {
            _configure = configure;
        }

        public LocalKeyOptionsExtension(Action<LocalKeyEncryptionKeyOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<LocalKeyEncryptionKeyOptions, LocalKeyEncryptionKeyOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<LocalKeyEncryptionKeyOptions, LocalKeyEncryptionKeyOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<LocalKeyEncryptionKeyOptions, LocalKeyEncryptionKeyOptionsValidator>(
                    _configureWithServices
                );
            }

            services.TryAddSingleton<IKeyEncryptionKeyProvider>(static sp => new LocalKeyEncryptionKeyProvider(
                sp.GetRequiredService<IOptions<LocalKeyEncryptionKeyOptions>>().Value
            ));
        }
    }
}
