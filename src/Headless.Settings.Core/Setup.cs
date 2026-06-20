// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Domain;
using Headless.Hosting.Initialization;
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
public static class SetupCoreSettings
{
    extension(IServiceCollection services)
    {
        public HeadlessSettingsBuilder AddHeadlessSettings(Action<HeadlessSettingsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessSettingsSetupBuilder(services);
            configure(setup);

            return _AddSettingsStorageCore(services, setup);
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
    }

    private static HeadlessSettingsBuilder _AddSettingsStorageCore(
        IServiceCollection serviceCollection,
        HeadlessSettingsSetupBuilder setup
    )
    {
        // Register the management core as part of storage setup so AddHeadlessSettings is the
        // single entry point. Guarded on ISettingManager so a repeated AddHeadlessSettings stays
        // safe (no duplicate value providers from the non-idempotent TypeList registrations in
        // _AddCore). The IStringEncryptionService guard inside _AddCore still fires when it is absent.
        if (!serviceCollection.Any(static s => s.ServiceType == typeof(ISettingManager)))
        {
            serviceCollection.Configure<SettingManagementOptions, SettingManagementOptionsValidator>(_ => { });
            _AddCore(serviceCollection);
        }

        serviceCollection.GuardSingleStorageProvider(
            setup.Extensions.Count,
            setup.Extensions.Count == 1 ? setup.Extensions.Single().GetType().FullName ?? "unknown" : "unknown",
            "Headless.Settings",
            ["UseEntityFramework", "UsePostgreSql", "UseSqlServer"],
            static name => new SettingsStorageProviderRegistration(name)
        );

        serviceCollection.Configure<SettingsStorageOptions>(options => setup.StorageOptions.CopyTo(options));

        foreach (var extension in setup.Extensions)
        {
            extension.AddServices(serviceCollection);
        }

        return new HeadlessSettingsBuilder(serviceCollection);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        if (!services.Any(s => s.ServiceType == typeof(IStringEncryptionService)))
        {
            throw new InvalidOperationException(
                $"{nameof(IStringEncryptionService)} must be registered before calling "
                    + $"{nameof(AddHeadlessSettings)}. "
                    + "Register it via AddStringEncryptionService(...) on IServiceCollection."
            );
        }

        services._AddCoreValueProvider();

        services.AddInitializerHostedService<SettingsInitializationBackgroundService>();

        services.TryAddTransient<
            IDomainEventHandler<EntityChangedEventData<SettingValueRecord>>,
            SettingValueCacheItemInvalidator
        >();

        services.TryAddSingleton<ISettingsErrorsDescriptor, DefaultSettingsErrorsDescriptor>();
        services.TryAddSingleton<ISettingEncryptionService, SettingEncryptionService>();

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

    private sealed record SettingsStorageProviderRegistration(string Provider);
}
