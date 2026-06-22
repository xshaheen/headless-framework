// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
using Headless.Domain;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Filters;
using Headless.Features.Models;
using Headless.Features.Repositories;
using Headless.Features.Resources;
using Headless.Features.Seeders;
using Headless.Features.ValueProviders;
using Headless.Features.Values;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Features;

/// <summary>DI entry point for the Headless Features Core module.</summary>
[PublicAPI]
public static class SetupCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Headless Features infrastructure (definitions, values, providers, cache invalidation, and hosted
        /// initializer) and applies the storage provider selected via <paramref name="configure"/>.
        /// </summary>
        /// <param name="configure">A delegate that configures the setup builder, including the storage provider.</param>
        /// <returns>A <see cref="HeadlessFeaturesBuilder"/> for further post-registration configuration.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessFeaturesBuilder AddHeadlessFeatures(Action<HeadlessFeaturesSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessFeaturesSetupBuilder(services);
            configure(setup);

            return _AddFeaturesStorageCore(services, setup);
        }

        /// <summary>Registers a custom <see cref="IFeatureDefinitionProvider"/> that contributes in-code feature definitions.</summary>
        /// <typeparam name="T">The provider type to register.</typeparam>
        /// <returns>The <see cref="IServiceCollection"/> to allow chaining.</returns>
        /// <remarks>
        /// Providers are invoked in registration order during the static-store build.
        /// If two providers declare a feature with the same name, the static store throws
        /// <see cref="InvalidOperationException"/> at startup.
        /// </remarks>
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

        /// <summary>Registers a custom <see cref="IFeatureValueReadProvider"/> into the provider chain (idempotent by provider type).</summary>
        /// <typeparam name="T">The provider type to register.</typeparam>
        /// <returns>The <see cref="IServiceCollection"/> to allow chaining.</returns>
        /// <remarks>
        /// Providers are consulted in reverse registration order (last-registered = highest priority).
        /// The built-in chain is <c>DefaultValue</c> → <c>Edition</c> → <c>Tenant</c>, so
        /// <c>Tenant</c> wins when multiple providers supply a value for the same feature.
        /// Calling this method more than once for the same <typeparamref name="T"/> is a no-op.
        /// </remarks>
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
        // Register the management core as part of storage setup so AddHeadlessFeatures is the
        // single entry point. Guarded on IFeatureManager so a repeated AddHeadlessFeatures stays
        // safe (no duplicate value providers from the non-idempotent registrations in _AddCore).
        if (!serviceCollection.Any(static s => s.ServiceType == typeof(IFeatureManager)))
        {
            serviceCollection.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(_ => { });
            _AddCore(serviceCollection);
        }

        serviceCollection.GuardSingleStorageProvider(
            setup.Extensions.Count,
            setup.Extensions.Count == 1 ? setup.Extensions.Single().GetType().FullName ?? "unknown" : "unknown",
            "Headless.Features",
            ["UseEntityFramework", "UsePostgreSql", "UseSqlServer"],
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
            IDomainEventHandler<EntityChangedEventData<FeatureValueRecord>>,
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
        services.TryAddSingleton<IFeatureValueStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FeatureManagementOptions>>().Value;
            var cache = string.IsNullOrEmpty(options.FeatureValueCacheName)
                ? sp.GetRequiredService<ICache>()
                : sp.GetRequiredService<ICacheProvider>().GetCache(options.FeatureValueCacheName);
            return new FeatureValueStore(
                sp.GetRequiredService<IFeatureDefinitionManager>(),
                sp.GetRequiredService<IFeatureValueRecordRepository>(),
                sp.GetRequiredService<IGuidGenerator>(),
                cache
            );
        });
        services.TryAddSingleton<IFeatureValueProviderManager, FeatureValueProviderManager>();
        services.TryAddTransient<IFeatureManager, FeatureManager>();

        services.AddSingleton<IMethodInvocationFeatureCheckerService, MethodInvocationFeatureCheckerService>();

        return services;
    }
}
