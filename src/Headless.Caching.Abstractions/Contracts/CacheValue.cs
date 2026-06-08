// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA1000 // Do not declare static members on generic types
// CA1815: equality is intentionally not part of CacheValue's contract — it is a read-result envelope,
// not a comparable key. Implementing it would compare cached payloads (EqualityComparer<T>.Default),
// which is a surprising and potentially expensive footgun. No call site compares instances.
#pragma warning disable CA1815 // Override equals and operator equals on value types
#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Cache value.</summary>
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
        if (isStale && !hasValue)
        {
            throw new ArgumentException("IsStale requires HasValue.", nameof(isStale));
        }

        Value = value;
        HasValue = hasValue;
        IsStale = isStale;
    }

    /// <summary>Gets the value.</summary>
    /// <value>The value.</value>
    public T? Value { get; }

    /// <summary>Gets a value indicating whether this <see cref="CacheValue{T}"/> has value.</summary>
    /// <value><see langword="true"/> if has value; otherwise, <see langword="false"/>.</value>
    public bool HasValue { get; }

    /// <summary>
    /// Gets a value indicating whether this value was served from a fail-safe stale reserve.
    /// </summary>
    /// <value><see langword="true"/> when fail-safe activated; otherwise, <see langword="false"/>.</value>
    public bool IsStale { get; }

    /// <summary>Gets a value indicating whether this <see cref="CacheValue{T}"/> is null.</summary>
    /// <value><see langword="true"/> if is null; otherwise, <see langword="false"/>.</value>
    [MemberNotNullWhen(false, nameof(Value))]
    public bool IsNull => Value is null;

    /// <summary>Gets the null.</summary>
    /// <value>The null.</value>
    public static CacheValue<T> Null { get; } = new(default, hasValue: true);

    /// <summary>Gets the no value.</summary>
    /// <value>The no value.</value>
    public static CacheValue<T> NoValue { get; } = new(default, hasValue: false);

    public override string ToString() => Value?.ToString() ?? "<null>";
}
