// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Domains;
using Framework.Permissions.Definitions;
using Framework.Permissions.Entities;
using Framework.Permissions.Filters;
using Framework.Permissions.GrantProviders;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Permissions.Seeders;
using Framework.Permissions.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Permissions;

[PublicAPI]
public static class AddPermissionsExtensions
{
    public static IServiceCollection AddPermissionsManagementCore(
        this IServiceCollection services,
        Action<PermissionManagementOptions>? setupAction = null
    )
    {
        services._AddCoreValueProvider();
        services.AddHostedService<PermissionsInitializationBackgroundService>();
        services.AddTransient<IGrantPermissionsSeedHelper, GrantPermissionsSeedHelper>();
        services.AddSingletonOptions<PermissionManagementOptions, PermissionManagementOptionsValidator>();

        if (setupAction is not null)
        {
            services.Configure(setupAction);
        }

        services.AddTransient<
            ILocalMessageHandler<EntityChangedEventData<PermissionGrantRecord>>,
            PermissionGrantCacheItemInvalidator
        >();

        // Definition Services
        /*
         * 1. You need to provide a storage implementation for `IPermissionDefinitionRecordRepository`
         * 2. Implement `IPermissionDefinitionProvider` to define your permissions in code
         *    and use `AddPermissionDefinitionProvider` to register it
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

        return services;
    }

    public static void AddPermissionDefinitionProvider<T>(this IServiceCollection services)
        where T : class, IPermissionDefinitionProvider
    {
        services.AddSingleton<T>();

        services.Configure<PermissionManagementProvidersOptions>(options =>
        {
            options.DefinitionProviders.Add<T>();
        });
    }

    public static void AddPermissionGrantProvider<T>(this IServiceCollection services)
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
    }

    public static IServiceCollection AddAlwaysAllowAuthorization(this IServiceCollection services)
    {
        services.ReplaceSingleton<IPermissionManager, AlwaysAllowPermissionManager>();
        services.ReplaceSingleton<IAuthorizationService, AlwaysAllowAuthorizationService>();

        services.ReplaceSingleton<
            IMethodInvocationAuthorizationService,
            AlwaysAllowMethodInvocationAuthorizationService
        >();

        return services;
    }

    private static void _AddCoreValueProvider(this IServiceCollection services)
    {
        services.Configure<PermissionManagementProvidersOptions>(options =>
        {
            // Last added provider has the highest priority
            options.GrantProviders.Add<RolePermissionGrantProvider>();
            options.GrantProviders.Add<UserPermissionGrantProvider>();
        });

        services.TryAddSingleton<IPermissionGrantProvider, RolePermissionGrantProvider>();
        services.TryAddSingleton<IPermissionGrantProvider, UserPermissionGrantProvider>();
    }
}
