// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Domains;
using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Framework.Settings.Helpers;
using Framework.Settings.Models;
using Framework.Settings.ValueProviders;
using Framework.Settings.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Framework.Settings;

[PublicAPI]
public static class AddSettingsExtensions
{
    /// <summary>
    /// Adds core setting management services to the host builder and registers default setting value providers.
    /// You should add TimeProvider, Cache, ResourceLock, and GuidGenerator implementations
    /// to be able to use this feature.
    /// </summary>
    public static IServiceCollection AddSettingsManagementCore(
        this IServiceCollection services,
        Action<SettingManagementOptions>? setupAction = null
    )
    {
        services.ConfigureSingleton(setupAction);
        services._AddSettingEncryption();
        services._AddCoreValueProvider();
        services.AddHostedService<SettingsInitializationBackgroundService>();
        services.AddTransient<
            ILocalMessageHandler<EntityChangedEventData<SettingValueRecord>>,
            SettingValueCacheItemInvalidator
        >();

        // Setting Definition Services
        /*
         * 1. You need to provide a storage implementation for `ISettingDefinitionRecordRepository`
         * 2. Implement `ISettingDefinitionProvider` to define your settings in code
         *    and use `AddSettingDefinitionProvider` to register it
         */
        services.TryAddSingleton<ISettingDefinitionSerializer, SettingDefinitionSerializer>();
        services.TryAddSingleton<IStaticSettingDefinitionStore, StaticSettingDefinitionStore>();
        services.TryAddSingleton<IDynamicSettingDefinitionStore, DynamicSettingDefinitionStore>();
        services.TryAddSingleton<ISettingDefinitionManager, SettingDefinitionManager>();

        // Setting Value Services
        /*
         * You need to provide a storage implementation for `ISettingValueRecordRepository`
         */
        services.TryAddSingleton<ISettingValueStore, SettingValueStore>();
        services.TryAddSingleton<ISettingValueProviderManager, SettingValueProviderManager>();
        services.TryAddTransient<ISettingProvider, SettingProvider>();

        return services;
    }

    public static void AddSettingDefinitionProvider<T>(this IServiceCollection services)
        where T : class, ISettingDefinitionProvider
    {
        services.AddSingleton<ISettingDefinitionProvider, T>();

        services.Configure<SettingManagementProvidersOptions>(options =>
        {
            options.DefinitionProviders.Add<T>();
        });
    }

    public static void AddSettingValueProvider<T>(this IServiceCollection services)
        where T : class, ISettingValueReadProvider
    {
        services.AddSingleton<ISettingValueReadProvider, T>();

        services.Configure<SettingManagementProvidersOptions>(options =>
        {
            if (!options.ValueProviders.Contains<T>())
            {
                options.ValueProviders.Add<T>();
            }
        });
    }

    private static void _AddSettingEncryption(this IServiceCollection services)
    {
        services.AddSingletonOptions<StringEncryptionSettings, StringEncryptionOptionsValidator>();
        services.TryAddSingleton<IStringEncryptionService, StringEncryptionService>();
        services.AddSingleton<ISettingEncryptionService, SettingEncryptionService>();
    }

    private static void _AddCoreValueProvider(this IServiceCollection services)
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

        services.AddSingleton<ISettingValueReadProvider, DefaultValueSettingValueProvider>();
        services.AddSingleton<ISettingValueReadProvider, ConfigurationSettingValueProvider>();
        services.AddSingleton<ISettingValueReadProvider, GlobalSettingValueProvider>();
        services.AddSingleton<ISettingValueReadProvider, TenantSettingValueProvider>();
    }
}
