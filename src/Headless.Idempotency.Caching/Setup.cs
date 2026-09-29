// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Headless.Idempotency.Caching;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Idempotency;

/// <summary>Chooses the application's <see cref="ICache" /> as the idempotency provider.</summary>
[PublicAPI]
public static class SetupIdempotencyCaching
{
    extension(HeadlessIdempotencySetupBuilder setup)
    {
        /// <summary>
        /// Keeps idempotency records in the application's remote cache with the default
        /// <see cref="CacheIdempotencyOptions" />: autonomous calls only, shared by every replica that uses the same
        /// cache.
        /// </summary>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// Needs a caching provider registered through <c>AddHeadlessCaching</c>; a shared one (Redis) is what makes
        /// the records hold across replicas. Only <see cref="IIdempotentOperations" /> works: every
        /// <c>unit.Idempotency</c> call is refused, because a cache cannot commit or roll back with a unit of work.
        /// Records last only as long as the cache keeps them, and leases run on the application clock.
        /// </remarks>
        public HeadlessIdempotencySetupBuilder UseCache()
        {
            setup.RegisterExtension(new CacheIdempotencyOptionsExtension());

            return setup;
        }

        /// <summary>Keeps idempotency records in a cache, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks><inheritdoc cref="UseCache(HeadlessIdempotencySetupBuilder)" path="/remarks" /></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseCache(Action<CacheIdempotencyOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new CacheIdempotencyOptionsExtension(configure: configure));

            return setup;
        }

        /// <summary>
        /// Keeps idempotency records in a cache, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks><inheritdoc cref="UseCache(HeadlessIdempotencySetupBuilder)" path="/remarks" /></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseCache(Action<CacheIdempotencyOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new CacheIdempotencyOptionsExtension(configureWithServices: configure));

            return setup;
        }

        /// <summary>
        /// Keeps idempotency records in a cache, binding <see cref="CacheIdempotencyOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks><inheritdoc cref="UseCache(HeadlessIdempotencySetupBuilder)" path="/remarks" /></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseCache(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new CacheIdempotencyOptionsExtension(configuration: configuration));

            return setup;
        }
    }
}

/// <summary>Registers the cache-backed record store.</summary>
/// <param name="configuration">The section bound to the options, if any.</param>
/// <param name="configure">The options delegate, if any.</param>
/// <param name="configureWithServices">The service-aware options delegate, if any.</param>
/// <param name="sharedCache">
/// A cache to use instead of resolving one, so several service providers in one test process share one cache the way
/// several replicas share one Redis.
/// </param>
internal sealed class CacheIdempotencyOptionsExtension(
    IConfiguration? configuration = null,
    Action<CacheIdempotencyOptions>? configure = null,
    Action<CacheIdempotencyOptions, IServiceProvider>? configureWithServices = null,
    ICache? sharedCache = null
) : IIdempotencyProviderOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        if (configuration is not null)
        {
            services.Configure<CacheIdempotencyOptions, CacheIdempotencyOptionsValidator>(configuration);
        }
        else if (configure is not null)
        {
            services.Configure<CacheIdempotencyOptions, CacheIdempotencyOptionsValidator>(configure);
        }
        else if (configureWithServices is not null)
        {
            services.Configure<CacheIdempotencyOptions, CacheIdempotencyOptionsValidator>(configureWithServices);
        }
        else
        {
            services.AddOptions<CacheIdempotencyOptions, CacheIdempotencyOptionsValidator>();
        }

        if (sharedCache is null)
        {
            // This package references only the cache abstraction, so a caching provider must be installed or every
            // idempotent call would fail at its first read.
            services.RequireRegisteredService<ICache>(
                requiredBy: "Headless idempotency cache provider",
                remedy: "Call AddHeadlessCaching(...) with a shared provider (UseRedis); UseInMemory keeps records "
                    + "per process."
            );
        }

        // Autonomous calls run in owned units the store begins, so the unit-of-work factory must exist whether or not
        // the host registered it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddUnitOfWork();

        services.TryAddSingleton<IIdempotencyRecordStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CacheIdempotencyOptions>>().Value;

            return new CacheIdempotencyRecordStore(
                sharedCache ?? _ResolveCache(provider, options),
                provider.GetRequiredService<IUnitOfWorkFactory>(),
                options,
                provider.GetRequiredService<TimeProvider>()
            );
        });
    }

    private static ICache _ResolveCache(IServiceProvider provider, CacheIdempotencyOptions options)
    {
        if (options.CacheName is { } name)
        {
            return provider.GetKeyedService<ICache>(name)
                ?? throw new InvalidOperationException(
                    $"No cache is registered under the name '{name}' that CacheIdempotencyOptions.CacheName names. "
                        + "Register it through AddHeadlessCaching(setup => setup.AddNamed(...))."
                );
        }

        // The remote tier when there is one: a hybrid cache's default ICache answers reads from a local tier that can
        // lag behind the shared one, which would make every compare-and-swap against the shared tier fail.
        return provider.GetService<IRemoteCache>() ?? provider.GetRequiredService<ICache>();
    }
}
