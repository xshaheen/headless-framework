// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Threading;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion;

namespace Framework.Caching;

/// <summary>
/// Implements <see cref="IHybridCache"/> using FusionCache as the underlying cache provider.
/// Provides L1 (memory) + L2 (distributed) hybrid caching with fail-safe and stampede protection.
/// </summary>
public sealed class FusionCacheAdapter(IFusionCache fusionCache, FusionCacheProviderOptions options) : IHybridCache
{
    public async Task<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var prefixedKey = _PrefixKey(key);
        var maybe = await fusionCache.TryGetAsync<T>(prefixedKey, token: cancellationToken).AnyContext();

        return maybe.HasValue
            ? new CacheValue<T>(maybe.Value, hasValue: true)
            : new CacheValue<T>(default, hasValue: false);
    }

    public async Task<CacheValue<T>> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        HybridCacheEntryOptions? entryOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);
        cancellationToken.ThrowIfCancellationRequested();

        var prefixedKey = _PrefixKey(key);
        var fcOptions = _MapEntryOptions(entryOptions);

        var result = await fusionCache
            .GetOrSetAsync<T?>(
                prefixedKey,
                async (_, ct) => await factory(ct).AnyContext(),
                fcOptions,
                cancellationToken
            )
            .AnyContext();

        return new CacheValue<T>(result, hasValue: true);
    }

    public async Task SetAsync<T>(
        string key,
        T? value,
        HybridCacheEntryOptions? entryOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var prefixedKey = _PrefixKey(key);
        var fcOptions = _MapEntryOptions(entryOptions);

        await fusionCache.SetAsync(prefixedKey, value, fcOptions, cancellationToken).AnyContext();
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var prefixedKey = _PrefixKey(key);
        await fusionCache.RemoveAsync(prefixedKey, token: cancellationToken).AnyContext();
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var prefixedKey = _PrefixKey(key);
        var maybe = await fusionCache.TryGetAsync<object>(prefixedKey, token: cancellationToken).AnyContext();

        return maybe.HasValue;
    }

    public async Task ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var prefixedKey = _PrefixKey(key);
        await fusionCache.ExpireAsync(prefixedKey, token: cancellationToken).AnyContext();
    }

    public void Dispose()
    {
        // FusionCache is typically managed by DI container, no need to dispose here
    }

    #region Private Helpers

    private string _PrefixKey(string key)
    {
        return string.IsNullOrEmpty(options.KeyPrefix) ? key : options.KeyPrefix + key;
    }

    private FusionCacheEntryOptions _MapEntryOptions(HybridCacheEntryOptions? entryOptions)
    {
        var fcOptions = new FusionCacheEntryOptions
        {
            Duration = entryOptions?.Duration ?? options.DefaultDuration,
            IsFailSafeEnabled = entryOptions?.EnableFailSafe ?? options.EnableFailSafe,
            FailSafeMaxDuration = entryOptions?.FailSafeMaxDuration ?? options.FailSafeMaxDuration,
            FactorySoftTimeout = entryOptions?.FactoryTimeout ?? options.FactoryTimeout,
            DistributedCacheSoftTimeout = options.DistributedCacheSoftTimeout,
            DistributedCacheHardTimeout = options.DistributedCacheHardTimeout,
            AllowBackgroundDistributedCacheOperations = options.AllowBackgroundDistributedCacheOperations,
            JitterMaxDuration = options.JitterMaxDuration,
            Priority = _MapPriority(entryOptions?.Priority ?? CacheItemPriority.Normal),
        };

        if (entryOptions?.EagerRefreshThreshold.HasValue is true)
        {
            fcOptions.EagerRefreshThreshold = entryOptions.EagerRefreshThreshold.Value;
        }

        return fcOptions;
    }

    private static CacheItemPriority _MapPriority(CacheItemPriority priority)
    {
        return priority switch
        {
            CacheItemPriority.Low => CacheItemPriority.Low,
            CacheItemPriority.Normal => CacheItemPriority.Normal,
            CacheItemPriority.High => CacheItemPriority.High,
            CacheItemPriority.NeverRemove => CacheItemPriority.NeverRemove,
            _ => CacheItemPriority.Normal,
        };
    }

    #endregion
}
