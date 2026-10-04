// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Provides per-execution context to a conditional cache factory. Exposes the last-known cached value and its validators
/// (<see cref="ETag"/> and <see cref="LastModifiedAt"/>) so the factory can perform conditional refresh and return
/// <see cref="NotModified"/> to extend the existing entry or <see cref="Modified(T, string?, DateTime?)"/> to replace it.
/// </summary>
/// <typeparam name="T">The cached value type.</typeparam>
/// <param name="staleValue">
/// The last-known-good cached value, or <see cref="CacheValue{T}.NoValue"/> when no physically retained entry exists.
/// </param>
[PublicAPI]
public sealed class CacheFactoryContext<T>(CacheValue<T> staleValue)
{
    /// <summary>Gets the cache key being populated, including any scoping applied by the underlying store.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets a value indicating whether a last-known-good value exists for <see cref="Key"/>. Returns <see langword="true"/>
    /// when the entry is logically expired but still physically retained.
    /// </summary>
    public bool HasStaleValue => StaleValue.HasValue;

    /// <summary>
    /// Gets the last-known-good cached value, or <see cref="CacheValue{T}.NoValue"/> when none exists.
    /// </summary>
    public CacheValue<T> StaleValue { get; } = staleValue;

    /// <summary>Gets the optional opaque entity tag stored with the existing entry.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the optional origin last-modified timestamp stored with the existing entry.</summary>
    public DateTime? LastModifiedAt { get; init; }

    /// <summary>
    /// Gets or sets the entry options applied when the factory result is written. Adaptive changes to duration
    /// take effect on write. Timeout options are consumed before the factory executes, so changing them inside
    /// the factory has no effect on the current call.
    /// </summary>
    public CacheEntryOptions Options { get; set; }

    /// <summary>
    /// Gets or sets the invalidation tags persisted with the entry. Initialized from the existing entry tags or
    /// from <see cref="CacheEntryOptions.Tags"/>. Setting this to <see langword="null"/> removes all tags.
    /// </summary>
    public IReadOnlyCollection<string>? Tags { get; set; }

    /// <summary>
    /// Reports that the origin value is unchanged. The existing cached value is restamped as fresh with current
    /// <see cref="Options"/>, preserving its value and validators.
    /// </summary>
    /// <exception cref="InvalidOperationException">No cached value exists to extend.</exception>
    public CacheFactoryResult<T> NotModified()
    {
        if (!HasStaleValue)
        {
            throw new InvalidOperationException(
                $"Cannot report NotModified for cache key '{Key}': no cached value exists to extend. "
                    + "Return Modified(value) when the cache has no last-known-good value."
            );
        }

        return new CacheFactoryResult<T> { IsNotModified = true };
    }

    /// <summary>Reports a new value that replaces the cached entry, optionally supplying fresh validators.</summary>
    /// <param name="value">The new value to cache.</param>
    /// <param name="eTag">The optional opaque entity tag describing the new value.</param>
    /// <param name="lastModifiedAt">The optional origin last-modified timestamp of the new value.</param>
    public CacheFactoryResult<T> Modified(T? value, string? eTag = null, DateTime? lastModifiedAt = null)
    {
        return new()
        {
            Value = value,
            ETag = eTag,
            LastModifiedAt = lastModifiedAt,
        };
    }
}
