// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Shared store-level operations composed by providers on top of <see cref="IFactoryCacheStore"/>.</summary>
[PublicAPI]
public static class FactoryCacheStoreExtensions
{
    /// <summary>
    /// Performs a direct options-based upsert: validates <paramref name="options"/> with the same rules as the
    /// factory coordinator, computes the fresh-write stamps once via <see cref="CacheEntryStamps.Compute"/>, and
    /// persists the entry with its <see cref="CacheEntryOptions.Tags"/>. The write is preceded by one entry read
    /// so dropped tags can be reconciled against provider reverse tag indexes — a documented non-hot-path cost;
    /// callers that need none of the per-entry option semantics should use the plain TTL upsert instead.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="store">The provider store.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache; <see langword="null"/> persists the null sentinel.</param>
    /// <param name="options">The cache entry options applied to the written entry.</param>
    /// <param name="timeProvider">The time provider used to stamp the entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async ValueTask UpsertEntryAsync<T>(
        this IFactoryCacheStore store,
        string key,
        T? value,
        CacheEntryOptions options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(store);
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(timeProvider);
        CacheEntryStamps.ValidateOptions(options);

        cancellationToken.ThrowIfCancellationRequested();

        // Read-before-write so a tag the previous entry carried but this write drops is removed from the
        // provider's reverse tag index together with the write.
        var existing = await store.TryGetEntryAsync<T>(key, cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stamps = CacheEntryStamps.Compute(options, now);
        var previousTags = existing.IsPhysicallyPresent(now) ? existing.Tags : null;

        var entry = new CacheStoreEntryWrite<T>
        {
            Value = value,
            IsNull = value is null,
            LogicalExpiresAt = stamps.LogicalExpiresAt,
            PhysicalExpiresAt = stamps.PhysicalExpiresAt,
            SlidingExpiration = options.SlidingExpiration,
            EagerRefreshAt = stamps.EagerRefreshAt,
            Tags = options.Tags,
            RemovedTags = CacheEntryStamps.ComputeRemovedTags(previousTags, options.Tags),
            // Per-call tier-write control for the direct upsert path. Hybrid honors these; single-tier providers
            // ignore the descriptor fields.
            SkipMemoryCacheWrite = options.SkipMemoryCacheWrite,
            SkipDistributedCacheWrite = options.SkipDistributedCacheWrite,
        };

        await store.SetEntryAsync(key, in entry, cancellationToken).ConfigureAwait(false);
    }
}
