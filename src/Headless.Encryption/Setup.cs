// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Encryption;

/// <summary>Registers field-level envelope encryption.</summary>
[PublicAPI]
public static class SetupEncryption
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IFieldEncryptor" />, <see cref="IDataKeyManager" />, the host-scope keys as an
        /// <c>EncryptionKeys</c> resource, and a warm-up that unwraps the host key behind readiness. Exactly one key
        /// store (<c>UsePostgreSql</c> or <c>UseSqlServer</c>) and exactly one key-encryption key (<c>UseLocalKey</c>
        /// or a key service provider) are required.
        /// </summary>
        /// <param name="configure">Chooses the providers and configures the key cache.</param>
        /// <returns>The service collection to support method chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No key store or key-encryption key, or more than one of either, was chosen, or encryption was already
        /// registered.
        /// </exception>
        public IServiceCollection AddHeadlessEncryption(Action<HeadlessEncryptionSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessEncryptionSetupBuilder(services);
            configure(setup);

            if (setup.KeyEncryptionKeyProviders.Count != 1)
            {
                throw new InvalidOperationException(
                    "Headless.Encryption requires exactly one key-encryption key. "
                        + (
                            setup.KeyEncryptionKeyProviders.Count == 0
                                ? "Call UseLocalKey or a key service provider's Use… member."
                                : "Multiple key-encryption keys were configured."
                        )
                );
            }

            services.GuardSingleStorageProvider(
                setup.KeyStores.Count,
                setup.KeyStores.Count == 1 ? setup.KeyStores[0].GetType().FullName ?? "unknown" : "unknown",
                "Headless.Encryption",
                ["UsePostgreSql", "UseSqlServer"],
                static name => new EncryptionKeyStoreRegistration(name)
            );

            services.AddOptions<EncryptionOptions, EncryptionOptionsValidator>();

            if (setup.OptionsConfigurator is not null)
            {
                services.Configure(setup.OptionsConfigurator);
            }

            // A null FieldContext.Scope resolves to the current tenant, so ICurrentTenant must follow
            // ICurrentTenant.Change. The NullCurrentTenant fallback ignores Change and would refuse every tenant-scoped
            // call; this swaps it for the AsyncLocal-backed tenant unless the host registered a real one.
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();

            // Cached keys expire on this clock, and the relational stores wait between deadlock retries on it.
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton(sp => new DataKeyCache(
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IOptions<EncryptionOptions>>().Value
            ));
            services.TryAddSingleton<DataKeyRing>();
            services.TryAddSingleton<IFieldEncryptor, FieldEncryptor>();
            services.TryAddSingleton<IDataKeyManager, DataKeyManager>();
            services.AddResourceProvisioner(static sp => new EncryptionKeysProvisioner(
                sp.GetRequiredService<IDataKeyManager>(),
                sp.GetRequiredService<IDataKeyStore>()
            ));
            services.AddWarmup<KeyEncryptionKeyWarmup>();

            setup.KeyStores[0].AddServices(services);
            setup.KeyEncryptionKeyProviders[0].AddServices(services);

            return services;
        }
    }

    private sealed record EncryptionKeyStoreRegistration(string Provider);
}
