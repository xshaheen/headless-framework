// Copyright (c) Mahmoud Shaheen. All rights reserved.

// CA1815: equality is intentionally omitted. This is a transient carrier returned from bulk reads and consumed
// field-by-field (Value, Expiration); it is never used as a dictionary key, set member, or compared for equality.
#pragma warning disable CA1815 // Override equals and operator equals on value types
namespace Headless.Caching;

/// <summary>
/// Pairs a cache read result with the remaining logical expiration of the entry.
/// </summary>
/// <typeparam name="T">The type of the cached value.</typeparam>
/// <param name="value">The cache read result.</param>
/// <param name="expiration">
/// The remaining logical expiration of the entry at read time, or <see langword="null"/>
/// when the entry carries no logical expiration metadata.
/// </param>
[PublicAPI]
public readonly struct CacheValueWithExpiration<T>(CacheValue<T> value, TimeSpan? expiration)
{
    /// <summary>Gets the cache read result.</summary>
    public CacheValue<T> Value { get; } = value;

    /// <summary>
    /// Gets the remaining logical expiration of the entry at read time.
    /// Returns <see langword="null"/> when the entry carries no logical expiration metadata.
    /// </summary>
    public TimeSpan? Expiration { get; } = expiration;
}
