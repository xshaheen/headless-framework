// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Definitions;
using Framework.Features.Entities;
using Framework.Features.Filters;
using Framework.Features.Models;
using Framework.Features.Seeders;
using Framework.Features.ValueProviders;
using Framework.Features.Values;
using Framework.Kernel.Domains;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Features;

[PublicAPI]
public static class AddFeaturesExtensions
{
    /// <summary>
    /// Adds core feature management services to the host builder and registers default feature value providers.
    /// You should add TimeProvider, Cache, ResourceLock, and GuidGenerator implementations
    /// to be able to use this feature.
    /// </summary>
    public static IServiceCollection AddFeaturesManagementCore(
        this IServiceCollection services,
        Action<FeatureManagementOptions>? setupAction = null
    )
    {
        services._AddCoreValueProviders();
        services.AddHostedService<FeaturesInitializationBackgroundService>();
        services.AddSingletonOptions<FeatureManagementOptions, FeatureManagementOptionsValidator>();

        if (setupAction is not null)
        {
            services.Configure(setupAction);
        }

        services.AddTransient<
            ILocalMessageHandler<EntityChangedEventData<FeatureValueRecord>>,
            FeatureValueCacheItemInvalidator
        >();

        // Definition Services
        /*
         * 1. You need to provide a storage implementation for `IFeatureDefinitionRecordRepository`
         * 2. Implement `IFeatureDefinitionProvider` to define your features in code
         *    and use `AddFeatureDefinitionProvider` to register it
         */
        services.TryAddSingleton<IFeatureDefinitionSerializer, FeatureDefinitionSerializer>();
        services.TryAddSingleton<IStaticFeatureDefinitionStore, StaticFeatureDefinitionStore>();
        services.TryAddSingleton<IDynamicFeatureDefinitionStore, DynamicFeatureDefinitionStore>();
        services.TryAddSingleton<IFeatureDefinitionManager, FeatureDefinitionManager>();

        // Value Services
        /*
         * You need to provide a storage implementation for `IFeatureValueRecordRepository`
         */
        services.TryAddSingleton<IFeatureValueStore, FeatureValueStore>();
        services.TryAddSingleton<IFeatureValueProviderManager, FeatureValueProviderManager>();
        services.TryAddTransient<IFeatureManager, FeatureManager>();

        services.AddSingleton<IMethodInvocationFeatureCheckerService, MethodInvocationFeatureCheckerService>();

        return services;
    }

    public static void AddFeatureDefinitionProvider<T>(this IServiceCollection services)
        where T : class, IFeatureDefinitionProvider
    {
        services.AddSingleton<T>();

        services.Configure<FeatureManagementProvidersOptions>(options =>
        {
            options.DefinitionProviders.Add<T>();
        });
    }

    public static void AddFeatureValueProvider<T>(this IServiceCollection services)
        where T : class, IFeatureValueReadProvider
    {
        services.AddSingleton<T>();

        services.Configure<FeatureManagementProvidersOptions>(options =>
        {
            if (!options.ValueProviders.Contains<T>())
            {
                options.ValueProviders.Add<T>();
            }
        });
    }

    private static void _AddCoreValueProviders(this IServiceCollection services)
    {
        services.Configure<FeatureManagementProvidersOptions>(options =>
        {
            // Last added provider has the highest priority
            options.ValueProviders.Add<DefaultValueFeatureValueProvider>();
            options.ValueProviders.Add<EditionFeatureValueProvider>();
            options.ValueProviders.Add<TenantFeatureValueProvider>();
        });

        services.AddSingleton<IFeatureValueReadProvider, DefaultValueFeatureValueProvider>();
        services.AddSingleton<IFeatureValueReadProvider, EditionFeatureValueProvider>();
        services.AddSingleton<IFeatureValueReadProvider, TenantFeatureValueProvider>();
    }
}
