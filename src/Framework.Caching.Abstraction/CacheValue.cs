// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CA1000 // Do not declare static members on generic types
namespace Framework.Caching;

/// <summary>Cache value.</summary>
/// <param name="value">Value.</param>
/// <param name="hasValue">If set to <see langword="true"/> has value.</param>
public sealed class CacheValue<T>(T? value, bool hasValue)
{
    /// <summary>Gets the value.</summary>
    /// <value>The value.</value>
    public T? Value { get; } = value;

    /// <summary>Gets a value indicating whether this <see cref="CacheValue{T}"/> has value.</summary>
    /// <value><see langword="true"/> if has value; otherwise, <see langword="false"/>.</value>
    public bool HasValue { get; } = hasValue;

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
