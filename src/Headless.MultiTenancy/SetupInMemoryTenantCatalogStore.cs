// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.MultiTenancy;

/// <summary>Provides the <c>UseInMemory</c> extension member on <see cref="HeadlessTenancyCatalogSetupBuilder"/>.</summary>
[PublicAPI]
public static class SetupInMemoryTenantCatalogStore
{
    /// <summary>Configures the in-memory tenant store, seeded from <paramref name="configure"/>.</summary>
    /// <param name="setup">The catalog setup builder.</param>
    /// <param name="configure">Delegate that adds seed <see cref="TenantInfo"/> entries.</param>
    /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static HeadlessTenancyCatalogSetupBuilder UseInMemory(
        this HeadlessTenancyCatalogSetupBuilder setup,
        Action<InMemoryTenantStoreOptions> configure
    )
    {
        Argument.IsNotNull(setup);
        Argument.IsNotNull(configure);

        setup.RegisterExtension(new InMemoryTenantStoreOptionsExtension(configure));

        return setup;
    }

    /// <summary>Configures the in-memory tenant store from a pre-built options instance.</summary>
    /// <param name="setup">The catalog setup builder.</param>
    /// <param name="options">The seed data.</param>
    /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static HeadlessTenancyCatalogSetupBuilder UseInMemory(
        this HeadlessTenancyCatalogSetupBuilder setup,
        InMemoryTenantStoreOptions options
    )
    {
        Argument.IsNotNull(setup);
        Argument.IsNotNull(options);

        return setup.UseInMemory(target => target.Tenants = options.Tenants);
    }

    /// <summary>
    /// <see cref="ITenantCatalogStorageOptionsExtension"/> that registers the in-memory store and its
    /// eagerly validated (<c>ValidateOnStart</c>) seed options.
    /// </summary>
    private sealed class InMemoryTenantStoreOptionsExtension(Action<InMemoryTenantStoreOptions> configure)
        : ITenantCatalogStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<InMemoryTenantStoreOptions, InMemoryTenantStoreOptionsValidator>(configure);

            // Singleton: the store is an immutable snapshot built once from seed options at construction
            // (unlike a future EF-backed store, which would need Scoped to match its DbContext).
            services.TryAddSingleton<InMemoryTenantStore>();
            services.TryAddSingleton<ITenantStore>(sp => sp.GetRequiredService<InMemoryTenantStore>());
            services.TryAddSingleton<ITenantDirectory>(sp => sp.GetRequiredService<InMemoryTenantStore>());
        }
    }
}
