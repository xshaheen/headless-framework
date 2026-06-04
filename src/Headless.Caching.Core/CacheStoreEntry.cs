// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Provider entry snapshot used by the factory cache coordinator.</summary>
/// <typeparam name="T">The cached value type.</typeparam>
/// <param name="Found">Whether the store contains an entry.</param>
/// <param name="IsNull">Whether the stored value is the cache null sentinel.</param>
/// <param name="Value">The cached value.</param>
/// <param name="LogicalExpiresAt">The timestamp after which normal reads treat the entry as stale.</param>
/// <param name="PhysicalExpiresAt">The timestamp after which the entry is no longer retained.</param>
[PublicAPI]
public readonly record struct CacheStoreEntry<T>(
    bool Found,
    bool IsNull,
    T? Value,
    DateTime? LogicalExpiresAt,
    DateTime? PhysicalExpiresAt
)
{
    /// <summary>Gets an entry representing a store miss.</summary>
    public static CacheStoreEntry<T> NotFound { get; } = new(
        Found: false,
        IsNull: false,
        Value: default,
        LogicalExpiresAt: null,
        PhysicalExpiresAt: null
    );
}

/// <summary>Shared expiration predicates over <see cref="CacheStoreEntry{T}"/> used by Core and providers.</summary>
[PublicAPI]
public static class CacheStoreEntryExtensions
{
    /// <summary>Returns whether the entry is present and not past its logical (stale) expiration.</summary>
    public static bool IsFresh<T>(this CacheStoreEntry<T> entry, DateTime now)
    {
        if (!entry.IsPhysicallyPresent(now))
        {
            return false;
        }

        return !entry.LogicalExpiresAt.HasValue || entry.LogicalExpiresAt.Value > now;
    }

    /// <summary>Returns whether the entry is present and not past its physical (retention) expiration.</summary>
    public static bool IsPhysicallyPresent<T>(this CacheStoreEntry<T> entry, DateTime now) =>
        entry.Found && (!entry.PhysicalExpiresAt.HasValue || entry.PhysicalExpiresAt.Value > now);

    /// <summary>Returns the earlier of two timestamps.</summary>
    public static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
}
