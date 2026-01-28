// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Filters;
using Headless.Features.Models;
using Headless.Features.Resources;
using Headless.Features.Seeders;
using Headless.Features.ValueProviders;
using Headless.Features.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Features;

[PublicAPI]
public static class CoreSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds core feature management services to the host builder and registers default feature value providers.
        /// You should add TimeProvider, Cache, ResourceLock, and GuidGenerator implementations
        /// to be able to use this feature.
        /// </summary>
        public IServiceCollection AddFeaturesManagementCore(
            Action<FeatureManagementOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(setupAction);

            return _AddCore(services);
        }

        /// <summary>
        /// Adds core feature management services to the host builder and registers default feature value providers.
        /// You should add TimeProvider, Cache, ResourceLock, and GuidGenerator implementations
        /// to be able to use this feature.
        /// </summary>
        public IServiceCollection AddFeaturesManagementCore(Action<FeatureManagementOptions>? setupAction = null)
        {
            services.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(setupAction);

            return _AddCore(services);
        }

        public IServiceCollection AddFeatureDefinitionProvider<T>()
            where T : class, IFeatureDefinitionProvider
        {
            services.AddSingleton<T>();

            services.Configure<FeatureManagementProvidersOptions>(options =>
            {
                options.DefinitionProviders.Add<T>();
            });

            return services;
        }

        public IServiceCollection AddFeatureValueProvider<T>()
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

            return services;
        }

        private void _AddCoreValueProviders()
        {
            services.Configure<FeatureManagementProvidersOptions>(options =>
            {
                // Last added provider has the highest priority
                options.ValueProviders.Add<DefaultValueFeatureValueProvider>();
                options.ValueProviders.Add<EditionFeatureValueProvider>();
                options.ValueProviders.Add<TenantFeatureValueProvider>();
            });

            services.AddSingleton<DefaultValueFeatureValueProvider>();
            services.AddSingleton<EditionFeatureValueProvider>();
            services.AddSingleton<TenantFeatureValueProvider>();
        }
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services._AddCoreValueProviders();
        services.AddHostedService<FeaturesInitializationBackgroundService>();

        services.AddTransient<
            ILocalMessageHandler<EntityChangedEventData<FeatureValueRecord>>,
            FeatureValueCacheItemInvalidator
        >();

        services.AddSingleton<IFeatureErrorsDescriptor, DefaultFeatureErrorsDescriptor>();

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
}
