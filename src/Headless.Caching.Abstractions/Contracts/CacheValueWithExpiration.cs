// Copyright (c) Mahmoud Shaheen. All rights reserved.

// CA1815: equality is intentionally omitted. This is a transient carrier returned from bulk reads and consumed
// field-by-field (Value, Expiration); it is never used as a dictionary key, set member, or compared for equality.
#pragma warning disable CA1815 // Override equals and operator equals on value types
#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Pairs a cache read result with the entry's remaining logical expiration so callers can use both
/// in a single round-trip (see <see cref="IRemoteCache.GetAllWithExpirationAsync{T}"/>).
/// </summary>
/// <typeparam name="T">The type of the cached value.</typeparam>
/// <remarks>Initializes a new instance of the <see cref="CacheValueWithExpiration{T}"/> struct.</remarks>
/// <param name="value">The cache read result.</param>
/// <param name="expiration">
/// The remaining logical expiration of the entry at the time of the read, or <see langword="null"/>
/// when the entry carries no logical expiration metadata (e.g., written by a legacy code path).
/// </param>
[PublicAPI]
public readonly struct CacheValueWithExpiration<T>(CacheValue<T> value, TimeSpan? expiration)
{
    /// <summary>Gets the cache read result.</summary>
    public CacheValue<T> Value { get; } = value;

    /// <summary>
    /// Gets the remaining logical expiration of the entry at the time of the read.
    /// <see langword="null"/> means the entry carries no logical expiration metadata.
    /// </summary>
    public TimeSpan? Expiration { get; } = expiration;
}
