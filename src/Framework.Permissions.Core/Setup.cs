// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;
using Framework.Permissions.Definitions;
using Framework.Permissions.Entities;
using Framework.Permissions.GrantProviders;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Permissions.Requirements;
using Framework.Permissions.Resources;
using Framework.Permissions.Seeders;
using Framework.Permissions.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Permissions;

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

        public IServiceCollection AddPermissionsManagementCore(
            Action<PermissionManagementOptions>? setupAction = null
        )
        {
            services.Configure<PermissionManagementOptions, PermissionManagementOptionsValidator>(setupAction);

            return _AddCore(services);
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

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services._AddCoreValueProvider();
        services.AddHostedService<PermissionsInitializationBackgroundService>();
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
        services.TryAddSingleton<IPermissionGrantStore, PermissionGrantStore>();
        services.TryAddSingleton<IPermissionGrantProviderManager, PermissionGrantProviderManager>();
        services.TryAddSingleton<IPermissionManager, PermissionManager>();

        services.AddSingleton<IAuthorizationHandler, PermissionRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, PermissionsRequirementHandler>();

        return services;
    }
}
