// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Security.KeyVault.Keys;
using Headless.Checks;
using Headless.Encryption.AzureKeyVault;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Encryption;

/// <summary>Chooses an Azure Key Vault key as the encryption key-encryption key.</summary>
/// <remarks>
/// The <see cref="KeyClient" /> is resolved from DI by default, so the application decides the vault and the
/// credential (<c>new KeyClient(vaultUri, new DefaultAzureCredential())</c>). Pass a <c>clientFactory</c> to use a
/// client this registration owns instead. The identity needs the key's wrapKey and unwrapKey permissions (the "Key
/// Vault Crypto User" role); it never needs to create or delete keys.
/// </remarks>
[PublicAPI]
public static class SetupEncryptionAzureKeyVault
{
    extension(HeadlessEncryptionSetupBuilder setup)
    {
        /// <summary>
        /// Wraps data keys with a Key Vault key, binding <see cref="AzureKeyVaultKeyEncryptionKeyOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the options.</param>
        /// <param name="clientFactory">
        /// Supplies the <see cref="KeyClient" />; <see langword="null" /> (the default) resolves it from DI.
        /// </param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseAzureKeyVault(
            IConfiguration configuration,
            Func<IServiceProvider, KeyClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(configuration);

            setup.RegisterKeyEncryptionKeyProvider(new AzureKeyVaultOptionsExtension(configuration, clientFactory));

            return setup;
        }

        /// <summary>Wraps data keys with a Key Vault key, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the options.</param>
        /// <param name="clientFactory">
        /// Supplies the <see cref="KeyClient" />; <see langword="null" /> (the default) resolves it from DI.
        /// </param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseAzureKeyVault(
            Action<AzureKeyVaultKeyEncryptionKeyOptions> configure,
            Func<IServiceProvider, KeyClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterKeyEncryptionKeyProvider(new AzureKeyVaultOptionsExtension(configure, clientFactory));

            return setup;
        }

        /// <summary>
        /// Wraps data keys with a Key Vault key, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the options with service resolution.</param>
        /// <param name="clientFactory">
        /// Supplies the <see cref="KeyClient" />; <see langword="null" /> (the default) resolves it from DI.
        /// </param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseAzureKeyVault(
            Action<AzureKeyVaultKeyEncryptionKeyOptions, IServiceProvider> configure,
            Func<IServiceProvider, KeyClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterKeyEncryptionKeyProvider(new AzureKeyVaultOptionsExtension(configure, clientFactory));

            return setup;
        }
    }

    private sealed class AzureKeyVaultOptionsExtension : IEncryptionProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<AzureKeyVaultKeyEncryptionKeyOptions>? _configure;
        private readonly Action<AzureKeyVaultKeyEncryptionKeyOptions, IServiceProvider>? _configureWithServices;
        private readonly Func<IServiceProvider, KeyClient>? _clientFactory;

        public AzureKeyVaultOptionsExtension(
            IConfiguration configuration,
            Func<IServiceProvider, KeyClient>? clientFactory
        )
        {
            _configuration = configuration;
            _clientFactory = clientFactory;
        }

        public AzureKeyVaultOptionsExtension(
            Action<AzureKeyVaultKeyEncryptionKeyOptions> configure,
            Func<IServiceProvider, KeyClient>? clientFactory
        )
        {
            _configure = configure;
            _clientFactory = clientFactory;
        }

        public AzureKeyVaultOptionsExtension(
            Action<AzureKeyVaultKeyEncryptionKeyOptions, IServiceProvider> configure,
            Func<IServiceProvider, KeyClient>? clientFactory
        )
        {
            _configureWithServices = configure;
            _clientFactory = clientFactory;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<AzureKeyVaultKeyEncryptionKeyOptions, AzureKeyVaultKeyEncryptionKeyOptionsValidator>(
                    _configuration
                );
            }
            else if (_configure is not null)
            {
                services.Configure<AzureKeyVaultKeyEncryptionKeyOptions, AzureKeyVaultKeyEncryptionKeyOptionsValidator>(
                    _configure
                );
            }
            else
            {
                services.Configure<AzureKeyVaultKeyEncryptionKeyOptions, AzureKeyVaultKeyEncryptionKeyOptionsValidator>(
                    _configureWithServices
                );
            }

            var clientFactory = _clientFactory;

            services.TryAddSingleton<IKeyEncryptionKeyProvider>(sp => new AzureKeyVaultKeyEncryptionKeyProvider(
                clientFactory?.Invoke(sp) ?? sp.GetRequiredService<KeyClient>(),
                sp.GetRequiredService<IOptions<AzureKeyVaultKeyEncryptionKeyOptions>>().Value
            ));
        }
    }
}
