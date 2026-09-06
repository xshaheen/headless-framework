// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.MultiTenancy;

/// <summary>Provides the <c>UseInMemory</c> extension members on <see cref="HeadlessTenancyCatalogSetupBuilder"/>.</summary>
[PublicAPI]
public static class SetupInMemoryTenantCatalogStore
{
    extension(HeadlessTenancyCatalogSetupBuilder setup)
    {
        /// <summary>Configures the in-memory tenant store, seeded from <paramref name="configure"/>.</summary>
        /// <param name="configure">Delegate that adds seed <see cref="TenantInfo"/> entries.</param>
        /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessTenancyCatalogSetupBuilder UseInMemory(Action<InMemoryTenantStoreOptions> configure)
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new InMemoryTenantStoreOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Configures the in-memory tenant store, applying <paramref name="configure"/> to the seed options
        /// with access to the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="InMemoryTenantStoreOptions"/> with service resolution.</param>
        /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessTenancyCatalogSetupBuilder UseInMemory(
            Action<InMemoryTenantStoreOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new InMemoryTenantStoreOptionsExtension(configure));

            return setup;
        }

        /// <summary>Configures the in-memory tenant store from a pre-built options instance.</summary>
        /// <param name="options">The seed data.</param>
        /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public HeadlessTenancyCatalogSetupBuilder UseInMemory(InMemoryTenantStoreOptions options)
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(options);

            return setup.UseInMemory(target => target.Tenants = options.Tenants);
        }
    }

    /// <summary>
    /// <see cref="ITenantCatalogStorageOptionsExtension"/> that registers the in-memory store and its
    /// eagerly validated (<c>ValidateOnStart</c>) seed options.
    /// </summary>
    private sealed class InMemoryTenantStoreOptionsExtension : ITenantCatalogStorageOptionsExtension
    {
        private readonly Action<InMemoryTenantStoreOptions>? _configure;
        private readonly Action<InMemoryTenantStoreOptions, IServiceProvider>? _configureWithServices;

        public InMemoryTenantStoreOptionsExtension(Action<InMemoryTenantStoreOptions> configure)
        {
            _configure = configure;
        }

        public InMemoryTenantStoreOptionsExtension(Action<InMemoryTenantStoreOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configure is not null)
            {
                services.Configure<InMemoryTenantStoreOptions, InMemoryTenantStoreOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<InMemoryTenantStoreOptions, InMemoryTenantStoreOptionsValidator>(
                    _configureWithServices
                );
            }

            // Singleton: the store is an immutable snapshot built once from seed options at construction
            // (unlike a future EF-backed store, which would need Scoped to match its DbContext).
            services.TryAddSingleton<InMemoryTenantStore>();
            services.TryAddSingleton<ITenantStore>(sp => sp.GetRequiredService<InMemoryTenantStore>());
            services.TryAddSingleton<ITenantDirectory>(sp => sp.GetRequiredService<InMemoryTenantStore>());
        }
    }
}
