// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Represents the outcome of a conditional cache factory execution. Create instances through
/// <see cref="CacheFactoryContext{T}.NotModified"/> or
/// <see cref="CacheFactoryContext{T}.Modified(T, string?, DateTime?)"/> rather than constructing them directly.
/// </summary>
/// <typeparam name="T">The cached value type.</typeparam>
[PublicAPI]
public readonly record struct CacheFactoryResult<T>
{
    /// <summary>
    /// Gets a value indicating whether the origin reported the cached value as still current. When <see langword="true"/>, the
    /// existing cached value is restamped as fresh and <see cref="Value"/> is ignored.
    /// </summary>
    public bool IsNotModified { get; init; }

    /// <summary>Gets the new value produced by the factory when <see cref="IsNotModified"/> is <see langword="false"/>.</summary>
    public T? Value { get; init; }

    /// <summary>Gets the optional opaque entity tag describing the produced value.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the optional origin timestamp at which the produced value was last modified.</summary>
    public DateTime? LastModifiedAt { get; init; }
}
