// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Headless.Idempotency.Caching;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Idempotency;

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
