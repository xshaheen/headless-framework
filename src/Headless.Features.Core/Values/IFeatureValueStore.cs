// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Repositories;

namespace Headless.Features.Values;

/// <summary>Persistent store for per-provider feature values.</summary>
/// <remarks>
/// The default implementation caches values per provider/key in a distributed cache. On the first
/// read for a given provider scope all values are fetched from the database and cached together,
/// so subsequent reads for the same scope are served from cache. Mutations (<see cref="SetAsync"/>,
/// <see cref="DeleteAsync"/>) update both the database and the cache immediately.
/// <para>
/// Every member throws <see cref="ArgumentException"/> before touching storage when a feature name, provider name, or
/// provider key is text some provider would not keep unchanged: surrounding white space (SQL Server ignores trailing spaces when
/// it compares keys, so <c>"acme"</c> and <c>"acme "</c> would address one row there and two on PostgreSQL), a NUL
/// character (PostgreSQL cannot store it), or an unpaired UTF-16 surrogate (SqlClient rewrites it to U+FFFD, merging
/// distinct keys). See <c>Argument.IsPortableKey</c>.
/// </para>
/// </remarks>
public interface IFeatureValueStore
{
    /// <summary>Returns the stored value for feature <paramref name="name"/> under <paramref name="providerName"/>/<paramref name="providerKey"/>, or <see langword="null"/> if not set.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="providerName">The provider name (e.g. <c>"Tenant"</c>).</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored string value, or <see langword="null"/> when no value has been persisted.</returns>
    Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Upserts the value of feature <paramref name="name"/> for <paramref name="providerName"/>/<paramref name="providerKey"/>.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores or clears several feature values under one provider scope in a single repository transaction:
    /// either every value changes or none does.
    /// </summary>
    /// <remarks>
    /// When another writer inserts or deletes one of these rows between the read and the save, the save fails as a
    /// whole; the store then reads the rows again, plans the batch against them, and retries, up to three attempts.
    /// The last writer's value wins. Any other failure is rethrown unchanged.
    /// </remarks>
    /// <param name="values">
    /// The values keyed by feature name. A <see langword="null"/> value removes the stored entry for that feature.
    /// </param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    Task SetAllAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes all stored values for feature <paramref name="name"/> matching <paramref name="providerName"/>/<paramref name="providerKey"/>.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}
