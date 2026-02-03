// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Domain;
using Headless.Settings.Definitions;
using Headless.Settings.Entities;
using Headless.Settings.Helpers;
using Headless.Settings.Models;
using Headless.Settings.Resources;
using Headless.Settings.Seeders;
using Headless.Settings.ValueProviders;
using Headless.Settings.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Settings;

[PublicAPI]
public static class CoreSettingsSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds core setting management services to the host builder and registers default setting value providers.
        /// You should add TimeProvider, Cache, DistributedLock, GuidGenerator, IConfiguration, ICurrentUser,
        /// and ICurrentTenant implementations to be able to use this feature.
        /// </summary>
        public IServiceCollection AddSettingsManagementCore(
            Action<StringEncryptionOptions> configureEncryption,
            Action<SettingManagementOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<SettingManagementOptions, SettingManagementOptionsValidator>(setupAction);

            return _AddCore(services, configureEncryption);
        }

        /// <summary>
        /// Adds core setting management services to the host builder and registers default setting value providers.
        /// You should add TimeProvider, Cache, DistributedLock, GuidGenerator, IConfiguration, ICurrentUser,
        /// and ICurrentTenant implementations to be able to use this feature.
        /// </summary>
        public IServiceCollection AddSettingsManagementCore(
            Action<StringEncryptionOptions> configureEncryption,
            Action<SettingManagementOptions>? setupAction = null
        )
        {
            services.Configure<SettingManagementOptions, SettingManagementOptionsValidator>(setupAction);

            return _AddCore(services, configureEncryption);
        }

        public IServiceCollection AddSettingDefinitionProvider<T>()
            where T : class, ISettingDefinitionProvider
        {
            services.AddSingleton<T>();

            services.Configure<SettingManagementProvidersOptions>(options =>
            {
                options.DefinitionProviders.Add<T>();
            });

            return services;
        }

        public IServiceCollection AddSettingValueProvider<T>() // Transient
            where T : class, ISettingValueReadProvider
        {
            services.AddSingleton<T>();

            services.Configure<SettingManagementProvidersOptions>(options =>
            {
                if (!options.ValueProviders.Contains<T>())
                {
                    options.ValueProviders.Add<T>();
                }
            });

            return services;
        }

        private void _AddCoreValueProvider()
        {
            services.Configure<SettingManagementProvidersOptions>(options =>
            {
                // Last added provider has the highest priority
                options.ValueProviders.Add<DefaultValueSettingValueProvider>();
                options.ValueProviders.Add<ConfigurationSettingValueProvider>();
                options.ValueProviders.Add<GlobalSettingValueProvider>();
                options.ValueProviders.Add<TenantSettingValueProvider>();
                options.ValueProviders.Add<UserSettingValueProvider>();
            });

            services.AddSingleton<DefaultValueSettingValueProvider>();
            services.AddSingleton<ConfigurationSettingValueProvider>();
            services.AddSingleton<GlobalSettingValueProvider>();
            services.AddSingleton<TenantSettingValueProvider>();
            services.AddSingleton<UserSettingValueProvider>();
        }

        private void _AddSettingEncryption(Action<StringEncryptionOptions> configureEncryption)
        {
            services.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(configureEncryption);
            services.AddSingletonOptionValue<StringEncryptionOptions>();
            services.TryAddSingleton<IStringEncryptionService, StringEncryptionService>();
            services.TryAddSingleton<ISettingEncryptionService, SettingEncryptionService>();
        }
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<StringEncryptionOptions> configureEncryption
    )
    {
        services._AddSettingEncryption(configureEncryption);
        services._AddCoreValueProvider();

        services.AddHostedService<SettingsInitializationBackgroundService>();

        services.TryAddTransient<
            ILocalMessageHandler<EntityChangedEventData<SettingValueRecord>>,
            SettingValueCacheItemInvalidator
        >();

        services.TryAddSingleton<ISettingsErrorsDescriptor, DefaultSettingsErrorsDescriptor>();

        // Definition Services
        /*
         * 1. You need to provide a storage implementation for `ISettingDefinitionRecordRepository`
         * 2. Implement `ISettingDefinitionProvider` to define your settings in code
         *    and use `AddSettingDefinitionProvider` to register it
         */
        services.TryAddSingleton<ISettingDefinitionSerializer, SettingDefinitionSerializer>();
        services.TryAddSingleton<IStaticSettingDefinitionStore, StaticSettingDefinitionStore>();
        services.TryAddSingleton<IDynamicSettingDefinitionStore, DynamicSettingDefinitionStore>();
        services.TryAddSingleton<ISettingDefinitionManager, SettingDefinitionManager>();

        // Value Services
        /*
         * You need to provide a storage implementation for `ISettingValueRecordRepository`
         */
        services.TryAddSingleton<ISettingValueStore, SettingValueStore>();
        services.TryAddSingleton<ISettingValueProviderManager, SettingValueProviderManager>();
        services.TryAddSingleton<ISettingManager, SettingManager>();

        return services;
    }
}
