// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Headless.Context;
using Headless.Features.Resources;
using Headless.Hosting;
using Headless.Messaging;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Features;

/// <summary>DI entry point for the Headless Features Core module.</summary>
[PublicAPI]
public static class SetupFeatures
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Headless Features infrastructure (definitions, values, providers, cache invalidation, and the
        /// <c>FeatureDefinitions</c> seeder) and applies the storage provider selected via <paramref name="configure"/>.
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

            services.Configure<FeatureManagementProvidersOptions>(options => options.DefinitionProviders.Add<T>());

            return services;
        }

        /// <summary>Registers a custom <see cref="IFeatureValueReadProvider"/> at the top of the provider chain, so it has the highest priority.</summary>
        /// <typeparam name="T">The provider type to register.</typeparam>
        /// <returns>The <see cref="IServiceCollection"/> to allow chaining.</returns>
        /// <remarks>
        /// The built-in chain is <c>DefaultValue</c> → <c>Edition</c> → <c>Tenant</c>, lowest priority first, and it is
        /// always present, whether this runs before or after <c>AddHeadlessFeatures</c>. Calling this method more than
        /// once for the same <typeparamref name="T"/> is a no-op. To place a provider lower in the chain, call
        /// <see cref="AddFeatureValueProviderBelow{T, THigher}"/>.
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

        /// <summary>
        /// Registers <typeparamref name="T"/> as a feature value provider directly below <typeparamref name="THigher"/>
        /// in the provider chain: a read at <typeparamref name="THigher"/> falls back to <typeparamref name="T"/>, and a
        /// read at <typeparamref name="T"/> falls back to the providers below it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The position is resolved after every <c>Configure</c> callback has run, so it does not depend on whether
        /// this runs before or after <c>AddHeadlessFeatures</c> or the registration of <typeparamref name="THigher"/>.
        /// A provider already in the chain moves to the new position.
        /// </para>
        /// <para>
        /// For example, <c>AddFeatureValueProviderBelow&lt;PlanFeatureValueProvider, TenantFeatureValueProvider&gt;()</c>
        /// gives the chain, highest priority first: Tenant, Plan, Edition, DefaultValue.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">The provider type to register.</typeparam>
        /// <typeparam name="THigher">The provider directly above <typeparamref name="T"/>.</typeparam>
        /// <returns>The <see cref="IServiceCollection"/> to allow chaining.</returns>
        /// <exception cref="InvalidOperationException">
        /// <typeparamref name="T"/> and <typeparamref name="THigher"/> are the same type; or, when the feature options
        /// are first resolved, <typeparamref name="THigher"/> is not in the value provider chain.
        /// </exception>
        public IServiceCollection AddFeatureValueProviderBelow<T, THigher>()
            where T : class, IFeatureValueReadProvider
            where THigher : class, IFeatureValueReadProvider
        {
            if (typeof(T) == typeof(THigher))
            {
                throw new InvalidOperationException(
                    $"Cannot place feature value provider {typeof(T).FullName} below itself."
                );
            }

            services.AddSingleton<T>();

            services.PostConfigure<FeatureManagementProvidersOptions>(options =>
            {
                var providers = options.ValueProviders;

                if (!providers.Contains<THigher>())
                {
                    throw new InvalidOperationException(
                        $"Cannot place feature value provider {typeof(T).FullName} below {typeof(THigher).FullName}, "
                            + "which is not in the feature value provider chain."
                    );
                }

                // The list runs lowest priority first, so the provider's slot is the anchor's current index.
                providers.Remove<T>();
                providers.Insert(providers.IndexOf(typeof(THigher)), typeof(T));
            });

            return services;
        }

        private void _AddCoreValueProviders()
        {
            // The chain itself is the initial value of FeatureManagementProvidersOptions.ValueProviders, so a custom
            // provider registered before AddHeadlessFeatures still lands relative to it.
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

        // The seeder's retry back-off runs on the system clock, never on a faked app TimeProvider that a test host
        // does not advance.
        services.AddSeeder(static sp =>
            ActivatorUtilities.CreateInstance<FeatureDefinitionsSeeder>(sp, TimeProvider.System)
        );

        // Loads the dynamic definitions after the start, behind readiness, so the first request does not pay for it.
        // It gates itself on the dynamic-store option, which is unknown until options resolve.
        services.AddWarmup<FeatureDefinitionsWarmup>();

        // The definition store keys its cross-instance lock on the application name and the manager stamps
        // its change announcements with the instance id; a host that never registered an identity gets one.
        services.AddHeadlessHostIdentity();

        // The change announcement's wire name is declared rather than convention-derived, so services sharing a
        // broker agree on it whatever naming conventions each configures. Inert when the host does not use messaging.
        services.ConfigureMessaging(static messaging =>
            messaging.Message<FeatureChangedMessage>(FeatureChangedMessage.MessageName, "1")
        );

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
        // One resolution of the value cache, shared by the store and by storage providers that evict entries on a
        // direct repository write; resolving it twice could hand them different instances.
        services.TryAddSingleton(FeatureValueCache.Create);
        services.TryAddSingleton<IFeatureValueStore>(sp =>
        {
            return new FeatureValueStore(
                sp.GetRequiredService<IFeatureDefinitionManager>(),
                sp.GetRequiredService<IFeatureValueRecordRepository>(),
                sp.GetRequiredService<IGuidGenerator>(),
                sp.GetRequiredService<FeatureValueCache>().Cache,
                sp.GetRequiredService<ILogger<FeatureValueStore>>()
            );
        });

        // FeatureValueStore reads through the root ICache (or, when FeatureValueCacheName is set, a named
        // cache from the same provider's ICacheProvider). This package references only
        // Headless.Caching.Abstractions, so both come from a caching provider package the host installs;
        // without one the host starts clean and the first feature check throws.
        services.RequireRegisteredService<ICache>(
            requiredBy: "Headless feature management value caching",
            remedy: "Call AddHeadlessCaching(...) with a provider (UseInMemory / UseRedis / UseHybrid)."
        );

        services.TryAddSingleton<IFeatureValueProviderManager, FeatureValueProviderManager>();
        // A singleton like the permission and setting managers: every dependency is one, and the authorization
        // handler below would otherwise build a new manager for every authorized request.
        services.TryAddSingleton<IFeatureManager, FeatureManager>();

        // unit.Features: the enlisted writes, resolved by the unit of work from this container. A unit-of-work feature
        // must be a singleton; a separate registration, so a host that replaces IFeatureManager keeps the framework's
        // enlisted path.
        services.AddUnitOfWork();
        services.TryAddSingleton<IUnitOfWorkFeatures, FeatureManager>();

        // The manager stamps an enlisted write's change announcement with the writing tenant. Same fallback as
        // Permissions, Settings, Messaging, and Jobs: an AsyncLocal-backed tenant, replaced by any real tenancy
        // registration.
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
        services.TryAddTransient<IClientVisibleFeaturesReader, ClientVisibleFeaturesReader>();

        services.AddSingleton<IMethodInvocationFeatureCheckerService, MethodInvocationFeatureCheckerService>();

        // [RequiresFeature] is authorization data, so this handler is the gate: no HTTP-specific registration
        // exists to forget. A singleton over the singleton IFeatureManager, so a replacement manager must be one too;
        // TryAddEnumerable keeps one handler.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, FeatureRequirementHandler>());

        // Registers the authorization services the handler runs under, and lets WebApplication add the
        // authorization middleware on its own when the host never calls UseAuthorization(). TryAdd-only, so a
        // host's own registrations win.
        services.AddAuthorizationCore();

        return services;
    }
}
