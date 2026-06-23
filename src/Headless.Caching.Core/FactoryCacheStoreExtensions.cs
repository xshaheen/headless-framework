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
    /// persists the entry with its <see cref="CacheEntryOptions.Tags"/>. Callers that need none of the per-entry
    /// option semantics should use the plain TTL upsert instead.
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stamps = CacheEntryStamps.Compute(options, now);

        var entry = new CacheStoreEntryWrite<T>
        {
            Value = value,
            IsNull = value is null,
            LogicalExpiresAt = stamps.LogicalExpiresAt,
            PhysicalExpiresAt = stamps.PhysicalExpiresAt,
            SlidingExpiration = options.SlidingExpiration,
            EagerRefreshAt = stamps.EagerRefreshAt,
            // Stamp the birth time so a prior tag/clear marker does not logically invalidate this fresh write
            // (Family-2 version-pinning compares CreatedAt against the newest applicable marker).
            CreatedAt = stamps.CreatedAt,
            Tags = options.Tags,
            // Per-call tier-write control for the direct upsert path. Hybrid honors these; single-tier providers
            // ignore the descriptor fields.
            SkipMemoryCacheWrite = options.SkipMemoryCacheWrite,
            SkipDistributedCacheWrite = options.SkipDistributedCacheWrite,
        };

        // Result discarded: unconditional upsert, no CAS guard.
        _ = await store.SetEntryAsync(key, in entry, cancellationToken).ConfigureAwait(false);
    }
}
