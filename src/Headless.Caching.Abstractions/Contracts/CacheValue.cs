// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable CA1000 // Do not declare static members on generic types
// CA1815: equality is intentionally not part of CacheValue's contract — it is a read-result envelope,
// not a comparable key. Implementing it would compare cached payloads (EqualityComparer<T>.Default),
// which is a surprising and potentially expensive footgun. No call site compares instances.
#pragma warning disable CA1815 // Override equals and operator equals on value types
#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Read result envelope returned by every cache read method. Distinguishes "present and fresh", "present
/// but stale (served from fail-safe reserve)", and "absent" without requiring the caller to use exceptions or
/// sentinel values for the absent case.
/// </summary>
/// <typeparam name="T">The type of the cached value.</typeparam>
/// <remarks>
/// A value type so the common synchronous read path (<c>ValueTask&lt;CacheValue&lt;T&gt;&gt;</c>) completes
/// without a heap allocation. <see langword="default"/> is a valid <see cref="NoValue"/> state
/// (<see cref="HasValue"/> and <see cref="IsStale"/> both <see langword="false"/>), so it never violates the
/// <c>IsStale ⇒ HasValue</c> invariant the constructor enforces.
/// </remarks>
[PublicAPI]
public readonly struct CacheValue<T>
{
    /// <summary>Initializes a new instance of the <see cref="CacheValue{T}"/> struct.</summary>
    /// <param name="value">Value.</param>
    /// <param name="hasValue">If set to <see langword="true"/> has value.</param>
    /// <param name="isStale">If set to <see langword="true"/>, the value was served from a fail-safe reserve.</param>
    public CacheValue(T? value, bool hasValue, bool isStale = false)
    {
        Argument.IsTrue(!isStale || hasValue, "IsStale requires HasValue.", nameof(isStale));

        Value = value;
        HasValue = hasValue;
        IsStale = isStale;
    }

    /// <summary>Gets the cached value. <see langword="null"/> when <see cref="HasValue"/> is <see langword="false"/>, or when a <see langword="null"/> was explicitly cached.</summary>
    /// <remarks>
    /// Always check <see cref="HasValue"/> before using this property; a <see langword="null"/> value here may
    /// mean either "absent from cache" or "a <see langword="null"/> was explicitly stored".
    /// </remarks>
    public T? Value { get; }

    /// <summary>Gets whether the cache entry was found (fresh or stale). <see langword="false"/> means absent from cache.</summary>
    public bool HasValue { get; }

    /// <summary>
    /// Gets a value indicating whether this value was served from a fail-safe stale reserve.
    /// </summary>
    /// <value><see langword="true"/> when fail-safe activated; otherwise, <see langword="false"/>.</value>
    public bool IsStale { get; }

    /// <summary>Gets whether <see cref="Value"/> is <see langword="null"/>.</summary>
    /// <remarks>An entry can be present in cache with a <see langword="null"/> value (explicitly stored null).</remarks>
    [MemberNotNullWhen(false, nameof(Value))]
    public bool IsNull => Value is null;

    /// <summary>A hit result carrying a cached <see langword="null"/> value (<see cref="HasValue"/> is <see langword="true"/>, <see cref="Value"/> is <see langword="null"/>).</summary>
    public static CacheValue<T> Null { get; } = new(default, hasValue: true);

    /// <summary>A miss result (absent from cache). <see cref="HasValue"/> is <see langword="false"/>.</summary>
    public static CacheValue<T> NoValue { get; } = new(default, hasValue: false);

    public override string ToString() => Value?.ToString() ?? "<null>";
}
