// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
using Headless.Domain;
using Headless.Hosting.Initialization;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.GrantProviders;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Headless.Permissions.Requirements;
using Headless.Permissions.Resources;
using Headless.Permissions.Seeders;
using Headless.Permissions.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Permissions;

[PublicAPI]
public static class PermissionsSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPermissionsManagementCore(
            Action<PermissionManagementOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<PermissionManagementOptions, PermissionManagementOptionsValidator>(setupAction);

            return _AddCore(services);
        }

        public IServiceCollection AddPermissionsManagementCore(Action<PermissionManagementOptions>? setupAction = null)
        {
            services.Configure<PermissionManagementOptions, PermissionManagementOptionsValidator>(setupAction);

            return _AddCore(services);
        }

        public HeadlessPermissionsBuilder AddHeadlessPermissions(Action<HeadlessPermissionsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessPermissionsSetupBuilder(services);
            configure(setup);

            return _AddPermissionsStorageCore(services, setup);
        }

        public IServiceCollection AddPermissionDefinitionProvider<T>()
            where T : class, IPermissionDefinitionProvider
        {
            services.AddSingleton<T>();

            services.Configure<PermissionManagementProvidersOptions>(options =>
            {
                options.DefinitionProviders.Add<T>();
            });

            return services;
        }

        public IServiceCollection AddPermissionGrantProvider<T>()
            where T : class, IPermissionGrantProvider
        {
            services.AddSingleton<T>();

            services.Configure<PermissionManagementProvidersOptions>(options =>
            {
                if (!options.GrantProviders.Contains<T>())
                {
                    options.GrantProviders.Add<T>();
                }
            });

            return services;
        }

        public IServiceCollection AddAlwaysAllowAuthorization()
        {
            services.AddOrReplaceSingleton<IPermissionManager, AlwaysAllowPermissionManager>();
            services.AddOrReplaceSingleton<IAuthorizationService, AlwaysAllowAuthorizationService>();

            return services;
        }

        private void _AddCoreValueProvider()
        {
            services.Configure<PermissionManagementProvidersOptions>(options =>
            {
                // Last-added provider has the highest priority
                options.GrantProviders.Add<RolePermissionGrantProvider>();
                options.GrantProviders.Add<UserPermissionGrantProvider>();
            });

            services.AddSingleton<RolePermissionGrantProvider>();
            services.AddSingleton<UserPermissionGrantProvider>();
        }
    }

    private static HeadlessPermissionsBuilder _AddPermissionsStorageCore(
        IServiceCollection serviceCollection,
        HeadlessPermissionsSetupBuilder setup
    )
    {
        serviceCollection.GuardSingleStorageProvider(
            setup.Extensions.Count,
            setup.Extensions.Count == 1 ? setup.Extensions.Single().GetType().FullName ?? "unknown" : "unknown",
            "Headless.Permissions",
            static name => new PermissionsStorageProviderRegistration(name)
        );

        serviceCollection.Configure<PermissionsStorageOptions>(options => setup.StorageOptions.CopyTo(options));

        foreach (var extension in setup.Extensions)
        {
            extension.AddServices(serviceCollection);
        }

        return new HeadlessPermissionsBuilder(serviceCollection);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services._AddCoreValueProvider();
        services.AddInitializerHostedService<PermissionsInitializationBackgroundService>();
        services.AddTransient<IGrantPermissionsSeedHelper, GrantPermissionsSeedHelper>();

        services.AddTransient<
            ILocalMessageHandler<EntityChangedEventData<PermissionGrantRecord>>,
            PermissionGrantCacheItemInvalidator
        >();

        services.TryAddSingleton<IPermissionErrorsDescriptor, DefaultPermissionErrorsDescriptor>();

        // Definition Services
        /*
         * 1. You need to provide a storage implementation for `IPermissionDefinitionRecordRepository`
         * 2. Implement `IPermissionDefinitionProvider` to define your permissions in code and use `AddPermissionDefinitionProvider` to register it
         */
        services.TryAddSingleton<IPermissionDefinitionSerializer, PermissionDefinitionSerializer>();
        services.TryAddSingleton<IStaticPermissionDefinitionStore, StaticPermissionDefinitionStore>();
        services.TryAddSingleton<IDynamicPermissionDefinitionStore, DynamicPermissionDefinitionStore>();
        services.TryAddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();

        // Value Services
        /*
         * You need to provide a storage implementation for `IPermissionGrantRecordRepository`
         */
        services.AddSingleton<ICache<PermissionGrantCacheItem>>(sp => new ScopedCache<PermissionGrantCacheItem>(
            sp.GetRequiredService<ICache>(),
            () => $"t:{sp.GetRequiredService<ICurrentTenant>().Id}"
        ));

        services.TryAddSingleton<IPermissionGrantStore, PermissionGrantStore>();
        services.TryAddSingleton<IPermissionGrantProviderManager, PermissionGrantProviderManager>();
        services.TryAddSingleton<IPermissionManager, PermissionManager>();

        services.AddSingleton<IAuthorizationHandler, PermissionRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, PermissionsRequirementHandler>();

        return services;
    }

    private sealed record PermissionsStorageProviderRegistration(string Provider);
}
