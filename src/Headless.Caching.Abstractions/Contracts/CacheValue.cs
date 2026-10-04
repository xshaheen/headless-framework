// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable CA1000 // Do not declare static members on generic types
// CA1815: equality is intentionally not part of CacheValue's contract — it is a read-result envelope,
// not a comparable key. Implementing it would compare cached payloads (EqualityComparer<T>.Default),
// which is a surprising and potentially expensive footgun. No call site compares instances.
#pragma warning disable CA1815 // Override equals and operator equals on value types
namespace Headless.Caching;

/// <summary>
/// Represents the result envelope returned by cache read operations. Distinguishes fresh values,
/// stale values served from fail-safe reserves, and cache misses without allocating exceptions or sentinel values.
/// </summary>
/// <typeparam name="T">The type of the cached value.</typeparam>
/// <remarks>
/// This struct allows synchronous reads to complete without heap allocations.
/// <see langword="default"/> represents a valid <see cref="NoValue"/> state where both
/// <see cref="HasValue"/> and <see cref="IsStale"/> are <see langword="false"/>.
/// </remarks>
[PublicAPI]
public readonly struct CacheValue<T>
{
    /// <summary>Initializes a new instance of the <see cref="CacheValue{T}"/> struct.</summary>
    /// <param name="value">The cached value.</param>
    /// <param name="hasValue">Indicates whether a cache entry was found.</param>
    /// <param name="isStale">Indicates whether the value was served from a fail-safe reserve.</param>
    public CacheValue(T? value, bool hasValue, bool isStale = false)
    {
        Argument.IsTrue(!isStale || hasValue, "IsStale requires HasValue.", nameof(isStale));

        Value = value;
        HasValue = hasValue;
        IsStale = isStale;
    }

    /// <summary>Gets the cached value. Returns <see langword="null"/> when <see cref="HasValue"/> is <see langword="false"/> or when <see langword="null"/> was stored.</summary>
    /// <remarks>
    /// Check <see cref="HasValue"/> before consuming this property because a <see langword="null"/> payload can represent an explicitly cached null value.
    /// </remarks>
    public T? Value { get; }

    /// <summary>Gets a value indicating whether the cache entry was found. Returns <see langword="false"/> when absent from cache.</summary>
    public bool HasValue { get; }

    /// <summary>
    /// Gets a value indicating whether this value was served from a fail-safe reserve.
    /// </summary>
    public bool IsStale { get; }

    /// <summary>Gets a value indicating whether <see cref="Value"/> is <see langword="null"/>.</summary>
    [MemberNotNullWhen(false, nameof(Value))]
    public bool IsNull => Value is null;

    /// <summary>Represents a hit result containing an explicitly cached <see langword="null"/> value.</summary>
    public static CacheValue<T> Null { get; } = new(default, hasValue: true);

    /// <summary>Represents a cache miss.</summary>
    public static CacheValue<T> NoValue { get; } = new(default, hasValue: false);

    public override string ToString()
    {
        return Value?.ToString() ?? "<null>";
    }
}
