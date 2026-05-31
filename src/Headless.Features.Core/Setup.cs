// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Checks;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Filters;
using Headless.Features.Models;
using Headless.Features.Resources;
using Headless.Features.Seeders;
using Headless.Features.ValueProviders;
using Headless.Features.Values;
using Headless.Hosting.Initialization;
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
        /// You should add TimeProvider, Cache, DistributedLock, and GuidGenerator implementations
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
        /// You should add TimeProvider, Cache, DistributedLock, and GuidGenerator implementations
        /// to be able to use this feature.
        /// </summary>
        public IServiceCollection AddFeaturesManagementCore(Action<FeatureManagementOptions>? setupAction = null)
        {
            services.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(setupAction);

            return _AddCore(services);
        }

        public HeadlessFeaturesBuilder AddHeadlessFeatures(Action<HeadlessFeaturesSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessFeaturesSetupBuilder(services);
            configure(setup);

            return _AddFeaturesStorageCore(services, setup);
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

    private static HeadlessFeaturesBuilder _AddFeaturesStorageCore(
        IServiceCollection serviceCollection,
        HeadlessFeaturesSetupBuilder setup
    )
    {
        // Ensure management core is registered so consumers no longer need a separate
        // AddFeaturesManagementCore() call. Guarded on IFeatureManager so calling both
        // AddFeaturesManagementCore and AddHeadlessFeatures stays safe (no duplicate value
        // providers from the non-idempotent registrations in _AddCore).
        if (!serviceCollection.Any(static s => s.ServiceType == typeof(IFeatureManager)))
        {
            serviceCollection.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(_ => { });
            _AddCore(serviceCollection);
        }

        serviceCollection.GuardSingleStorageProvider(
            setup.Extensions.Count,
            setup.Extensions.Count == 1 ? setup.Extensions.Single().GetType().FullName ?? "unknown" : "unknown",
            "Headless.Features",
            static name => new FeaturesStorageProviderRegistration(name)
        );

        serviceCollection.Configure<FeaturesStorageOptions>(options => setup.StorageOptions.CopyTo(options));

        foreach (var extension in setup.Extensions)
        {
            extension.AddServices(serviceCollection);
        }

        return new HeadlessFeaturesBuilder(serviceCollection);
    }

    private sealed record FeaturesStorageProviderRegistration(string Provider);

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services._AddCoreValueProviders();
        services.AddInitializerHostedService<FeaturesInitializationBackgroundService>();

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
