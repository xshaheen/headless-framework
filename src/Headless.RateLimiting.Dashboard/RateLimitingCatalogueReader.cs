// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.RateLimiting;

/// <summary>Builds the catalogue once, from options and registrations only, so serving it costs no I/O.</summary>
internal static class RateLimitingCatalogueReader
{
    public const string NamedStore = "named";
    public const string RemoteStore = "remote";
    public const string DefaultStore = "default";

    public static RateLimitingCatalogue Create(
        AttemptLimiterOptions options,
        IAttemptLimiter limiter,
        IServiceProvider provider
    )
    {
        RateLimitingPurposeView[] purposes =
        [
            .. options
                .Purposes.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new RateLimitingPurposeView(
                    pair.Key,
                    pair.Value.Limit,
                    (long)pair.Value.Window.TotalSeconds
                )),
        ];
        var shortest = purposes.OrderBy(static purpose => purpose.WindowSeconds).FirstOrDefault();

        // Only the built-in limiter's store selection is known: the cache CacheName names, else the remote tier when
        // one is registered, else the default cache. A custom limiter keeps its counters wherever it chooses.
        if (limiter.GetType().Assembly != typeof(IAttemptLimiter).Assembly)
        {
            return new RateLimitingCatalogue(purposes, shortest, CounterStore: null, CacheName: null);
        }

        if (options.CacheName is { } cacheName)
        {
            return new RateLimitingCatalogue(purposes, shortest, NamedStore, cacheName);
        }

        var isRemote = provider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IRemoteCache));

        return new RateLimitingCatalogue(purposes, shortest, isRemote ? RemoteStore : DefaultStore, CacheName: null);
    }
}
