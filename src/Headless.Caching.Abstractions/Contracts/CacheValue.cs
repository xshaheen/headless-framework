// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA1000 // Do not declare static members on generic types
#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Cache value.</summary>
[PublicAPI]
public sealed class CacheValue<T>
{
    /// <summary>Initializes a new instance of the <see cref="CacheValue{T}"/> class.</summary>
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
