// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
using Headless.Settings.Definitions;
using Headless.Settings.Entities;
using Headless.Settings.Models;
using Headless.Settings.Repositories;
using Microsoft.Extensions.Options;

namespace Headless.Settings.Values;

/// <summary>
/// Persistence and caching layer for raw setting values. Abstracts repository access and manages
/// the <see cref="SettingValueCacheItem"/> cache so callers never interact with the store directly.
/// </summary>
/// <remarks>
/// Every member throws <see cref="ArgumentException"/> before touching storage when a setting name, provider name, or
/// provider key is text some provider would not keep unchanged: surrounding white space (SQL Server ignores trailing spaces when
/// it compares keys, so <c>"acme"</c> and <c>"acme "</c> would address one row there and two on PostgreSQL), a NUL
/// character (PostgreSQL cannot store it), or an unpaired UTF-16 surrogate (SqlClient rewrites it to U+FFFD, merging
/// distinct keys). See <c>Argument.IsPortableKey</c>.
/// </remarks>
public interface ISettingValueStore
{
    /// <summary>Returns the stored value for the given setting, provider, and key, or <see langword="null"/> if not set.</summary>
    /// <param name="name">The setting name.</param>
    /// <param name="providerName">The provider name (e.g. <c>Global</c>, <c>Tenant</c>).</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/> for global providers.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The stored value, or <see langword="null"/> if not found.</returns>
    Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all setting values stored for the given provider and key, bypassing the per-name cache.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>All persisted values for the provider/key pair.</returns>
    Task<List<SettingValue>> GetAllProviderValuesAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the stored values for the specified setting <paramref name="names"/>.</summary>
    /// <param name="names">The set of setting names to retrieve.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A list of <see cref="SettingValue"/> entries; a <see langword="null"/> <c>Value</c> means the setting has no stored entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="names"/> is empty.</exception>
    Task<List<SettingValue>> GetAllAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Persists or updates the value for a setting.</summary>
    /// <param name="name">The setting name.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores or clears several setting values under one provider scope in a single repository transaction:
    /// either every value changes or none does.
    /// </summary>
    /// <remarks>
    /// When another writer inserts or deletes one of these rows between the read and the save, the save fails as a
    /// whole; the store then reads the rows again, plans the batch against them, and retries, up to three attempts.
    /// The last writer's value wins. Any other failure is rethrown unchanged.
    /// </remarks>
    /// <param name="values">
    /// The values keyed by setting name. A <see langword="null"/> value removes the stored entry for that setting.
    /// </param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    Task SetAllAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes the stored value for a setting and invalidates its cache entry.</summary>
    /// <param name="name">The setting name.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}
