// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Per-execution context handed to a conditional cache factory. The factory coordinator creates one instance per
/// factory execution, exposing the last-known cached value and its validators (<see cref="ETag"/>,
/// <see cref="LastModifiedAt"/>) so the factory can perform a conditional refresh: ask the origin "has this
/// changed since?" and return <see cref="NotModified"/> to extend the existing entry without re-transferring the
/// value, or <see cref="Modified(T, string?, DateTime?)"/> to replace it.
/// </summary>
/// <typeparam name="T">The cached value type.</typeparam>
/// <remarks>Initializes a new instance of the <see cref="CacheFactoryContext{T}"/> class.</remarks>
/// <param name="staleValue">
/// The last-known-good cached value, or <see cref="CacheValue{T}.NoValue"/> when no physically-retained entry
/// exists. Contexts are normally created by the factory cache coordinator, one per factory execution.
/// </param>
[PublicAPI]
public sealed class CacheFactoryContext<T>(CacheValue<T> staleValue)
{
    /// <summary>Gets the cache key being populated, as seen by the underlying store (including any key scoping).</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets whether a last-known-good value exists for <see cref="Key"/>. This is <see langword="true"/> even when
    /// the entry is logically expired, as long as it is still physically retained (e.g. a fail-safe reserve).
    /// </summary>
    public bool HasStaleValue => StaleValue.HasValue;

    /// <summary>
    /// Gets the last-known-good cached value (a cached <see langword="null"/> yields a value with
    /// <see cref="CacheValue{T}.HasValue"/> <see langword="true"/> and a <see langword="null"/>
    /// <see cref="CacheValue{T}.Value"/>), or <see cref="CacheValue{T}.NoValue"/> when none exists.
    /// </summary>
    public CacheValue<T> StaleValue { get; } = staleValue;

    /// <summary>Gets the opaque entity tag stored with the existing entry, if any.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the origin last-modified timestamp stored with the existing entry, if any.</summary>
    public DateTime? LastModifiedAt { get; init; }

    /// <summary>
    /// Gets or sets the entry options applied when the factory result is written (adaptive caching). The factory
    /// may replace these before returning — for example shortening <see cref="CacheEntryOptions.Duration"/> for a
    /// value it knows changes soon. The replacement is re-validated before the write; an invalid adaptive mutation
    /// (e.g. a non-positive duration) throws <see cref="ArgumentOutOfRangeException"/> after the factory has run
    /// and nothing is written. The factory-timeout family (<see cref="CacheEntryOptions.FactorySoftTimeout"/>,
    /// <see cref="CacheEntryOptions.FactoryHardTimeout"/>, <see cref="CacheEntryOptions.LockTimeout"/>) is
    /// consumed before the factory runs, so adaptive changes to those fields have no effect on the current call.
    /// </summary>
    public CacheEntryOptions Options { get; set; }

    /// <summary>
    /// Gets or sets the invalidation tags persisted with the entry. Initialized from the existing entry's tags when
    /// one exists (carry-forward), or from <see cref="CacheEntryOptions.Tags"/> when the call supplies them (call
    /// tags win over carried tags). Setting this to <see langword="null"/> inside the factory discards any carried
    /// tags so the written entry has none. Mutations are persisted on both modified and not-modified writes.
    /// </summary>
    public IReadOnlyCollection<string>? Tags { get; set; }

    /// <summary>
    /// Reports that the origin value is unchanged: the existing cached value is re-stamped as fresh with the
    /// current <see cref="Options"/>, preserving its value and validators.
    /// </summary>
    /// <exception cref="InvalidOperationException">No cached value exists to extend (<see cref="HasStaleValue"/> is <see langword="false"/>).</exception>
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

    /// <summary>Reports a new value (optionally with fresh validators) that replaces the cached entry.</summary>
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
