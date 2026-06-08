// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Store primitive used by <see cref="FactoryCacheCoordinator"/>.</summary>
[PublicAPI]
public interface IFactoryCacheStore
{
    /// <summary>Attempts to get an entry with its logical and physical expiration metadata.</summary>
    /// <remarks>
    /// Implementations must return entries that are still physically present even when logical expiration has
    /// passed. The coordinator uses that state as a fail-safe stale candidate. Return
    /// <see cref="CacheStoreEntry{T}.NotFound"/> only when the entry is missing or physically expired.
    /// Expiration timestamps are UTC. When <see cref="CacheStoreEntry{T}.IsNull"/> is <see langword="true"/>,
    /// <see cref="CacheStoreEntry{T}.Value"/> is ignored and the coordinator returns a cached null value.
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken);

    /// <summary>Sets an entry with explicit logical and physical expiration metadata.</summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The cached value.</param>
    /// <param name="isNull">Whether the stored value is the cache null sentinel.</param>
    /// <param name="logicalExpiresAt">The logical expiration timestamp.</param>
    /// <param name="physicalExpiresAt">The physical expiration timestamp.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask SetEntryAsync<T>(
        string key,
        T? value,
        bool isNull,
        DateTime logicalExpiresAt,
        DateTime physicalExpiresAt,
        CancellationToken cancellationToken
    );
}
