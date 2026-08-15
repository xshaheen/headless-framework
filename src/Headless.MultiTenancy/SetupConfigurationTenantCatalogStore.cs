// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.MultiTenancy;

/// <summary>Provides the <c>UseConfiguration</c> extension member trio on <see cref="HeadlessTenancyCatalogSetupBuilder"/>.</summary>
[PublicAPI]
public static class SetupConfigurationTenantCatalogStore
{
    /// <summary>
    /// Configures the configuration-backed tenant store, binding <see cref="ConfigurationTenantStoreOptions"/>
    /// from <paramref name="configuration"/> once at startup (for example a scoped
    /// <c>Headless:MultiTenancy:Tenants</c> section obtained via <c>IConfiguration.GetSection(...)</c>).
    /// Reload requires a process restart (KTD7) — there is no change-token re-binding.
    /// </summary>
    /// <param name="setup">The catalog setup builder.</param>
    /// <param name="configuration">The configuration to bind.</param>
    /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    public static HeadlessTenancyCatalogSetupBuilder UseConfiguration(
        this HeadlessTenancyCatalogSetupBuilder setup,
        IConfiguration configuration
    )
    {
        Argument.IsNotNull(setup);
        Argument.IsNotNull(configuration);

        setup.RegisterExtension(new ConfigurationTenantStoreOptionsExtension(configuration));

        return setup;
    }

    /// <summary>Configures the configuration-backed tenant store, applying <paramref name="configure"/> to the seed options.</summary>
    /// <param name="setup">The catalog setup builder.</param>
    /// <param name="configure">Delegate that adds seed <see cref="ConfigurationTenantSeed"/> entries.</param>
    /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static HeadlessTenancyCatalogSetupBuilder UseConfiguration(
        this HeadlessTenancyCatalogSetupBuilder setup,
        Action<ConfigurationTenantStoreOptions> configure
    )
    {
        Argument.IsNotNull(setup);
        Argument.IsNotNull(configure);

        setup.RegisterExtension(new ConfigurationTenantStoreOptionsExtension(configure));

        return setup;
    }

    /// <summary>
    /// Configures the configuration-backed tenant store, applying <paramref name="configure"/> to the seed
    /// options with access to the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="setup">The catalog setup builder.</param>
    /// <param name="configure">Delegate that configures <see cref="ConfigurationTenantStoreOptions"/> with service resolution.</param>
    /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static HeadlessTenancyCatalogSetupBuilder UseConfiguration(
        this HeadlessTenancyCatalogSetupBuilder setup,
        Action<ConfigurationTenantStoreOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(setup);
        Argument.IsNotNull(configure);

        setup.RegisterExtension(new ConfigurationTenantStoreOptionsExtension(configure));

        return setup;
    }

    /// <summary>
    /// <see cref="ITenantCatalogStorageOptionsExtension"/> that registers the configuration-backed store and
    /// its eagerly validated (<c>ValidateOnStart</c>) seed options.
    /// </summary>
    private sealed class ConfigurationTenantStoreOptionsExtension : ITenantCatalogStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<ConfigurationTenantStoreOptions>? _configure;
        private readonly Action<ConfigurationTenantStoreOptions, IServiceProvider>? _configureWithServices;

        public ConfigurationTenantStoreOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ConfigurationTenantStoreOptionsExtension(Action<ConfigurationTenantStoreOptions> configure)
        {
            _configure = configure;
        }

        public ConfigurationTenantStoreOptionsExtension(
            Action<ConfigurationTenantStoreOptions, IServiceProvider> configure
        )
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<ConfigurationTenantStoreOptions, ConfigurationTenantStoreOptionsValidator>(
                    _configuration
                );
            }
            else if (_configure is not null)
            {
                services.Configure<ConfigurationTenantStoreOptions, ConfigurationTenantStoreOptionsValidator>(
                    _configure
                );
            }
            else
            {
                services.Configure<ConfigurationTenantStoreOptions, ConfigurationTenantStoreOptionsValidator>(
                    _configureWithServices
                );
            }

            // Singleton: the store is an immutable snapshot bound once from IOptions<T> at construction
            // (KTD7) — matching InMemoryTenantStore; unlike a future EF-backed store, which would need
            // Scoped to match its DbContext.
            services.TryAddSingleton<ConfigurationTenantStore>();
            services.TryAddSingleton<ITenantStore>(sp => sp.GetRequiredService<ConfigurationTenantStore>());
            services.TryAddSingleton<ITenantDirectory>(sp => sp.GetRequiredService<ConfigurationTenantStore>());
        }
    }
}
