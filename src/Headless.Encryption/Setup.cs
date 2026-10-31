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
            services.TryAddSingleton(static sp => new KeyEncryptionKeyProviders(
                sp.GetRequiredService<IKeyEncryptionKeyProvider>(),
                sp.GetServices<PreviousKeyEncryptionKeyProvider>()
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

            foreach (var previous in setup.PreviousKeyEncryptionKeyProviders)
            {
                _AddPrevious(services, previous);
            }

            return services;
        }
    }

    /// <summary>
    /// Registers a previous provider's services as its <c>Use…</c> member would, except that its
    /// <see cref="IKeyEncryptionKeyProvider" /> becomes a decrypt-only <see cref="PreviousKeyEncryptionKeyProvider" />, so
    /// providers need no separate registration path for this role.
    /// </summary>
    private static void _AddPrevious(IServiceCollection services, IEncryptionProviderOptionsExtension previous)
    {
        var captured = new ServiceCollection();
        previous.AddServices(captured);

        foreach (var descriptor in captured)
        {
            if (descriptor.ServiceType == typeof(IKeyEncryptionKeyProvider) && !descriptor.IsKeyedService)
            {
                services.AddSingleton(sp => new PreviousKeyEncryptionKeyProvider(
                    (IKeyEncryptionKeyProvider)_Create(descriptor, sp)
                ));
            }
            else if (descriptor.IsKeyedService || _IsOptionsPipeline(descriptor.ServiceType))
            {
                // Options configuration and validation compose; the previous provider's own options type keeps them.
                services.Add(descriptor);
            }
            else
            {
                services.TryAdd(descriptor);
            }
        }
    }

    private static bool _IsOptionsPipeline(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition() is var definition
        && (
            definition == typeof(IConfigureOptions<>)
            || definition == typeof(IPostConfigureOptions<>)
            || definition == typeof(IValidateOptions<>)
            || definition == typeof(IOptionsChangeTokenSource<>)
        );

    private static object _Create(ServiceDescriptor descriptor, IServiceProvider services) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(services)
        ?? ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!);

    private sealed record EncryptionKeyStoreRegistration(string Provider);
}
