// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Outcome of a conditional cache factory execution (the HTTP-304 pattern). Create instances through
/// <see cref="CacheFactoryContext{T}.NotModified"/> or
/// <see cref="CacheFactoryContext{T}.Modified(T, string?, DateTime?)"/> rather than constructing them directly.
/// </summary>
/// <typeparam name="T">The cached value type.</typeparam>
[PublicAPI]
public readonly record struct CacheFactoryResult<T>
{
    /// <summary>
    /// Gets whether the origin reported the cached value as still current. When <see langword="true"/>, the
    /// existing cached value is re-stamped as fresh and <see cref="Value"/> is ignored.
    /// </summary>
    public bool IsNotModified { get; init; }

    /// <summary>Gets the new value produced by the factory when <see cref="IsNotModified"/> is <see langword="false"/>.</summary>
    public T? Value { get; init; }

    /// <summary>Gets the optional opaque entity tag describing the produced value.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the optional timestamp at which the produced value was last modified at its origin.</summary>
    public DateTime? LastModifiedAt { get; init; }
}
