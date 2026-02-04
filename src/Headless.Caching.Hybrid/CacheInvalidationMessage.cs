// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Message published to notify other instances about cache invalidation.
/// </summary>
[PublicAPI]
public sealed record CacheInvalidationMessage
{
    /// <summary>
    /// ID of the instance that originated this invalidation.
    /// Used to filter out self-originated messages to prevent echo.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>Single key to invalidate. Mutually exclusive with <see cref="Prefix"/> and <see cref="FlushAll"/>.</summary>
    public string? Key { get; init; }

    /// <summary>Multiple keys to invalidate. Mutually exclusive with <see cref="Prefix"/> and <see cref="FlushAll"/>.</summary>
    public string[]? Keys { get; init; }

    /// <summary>Prefix for prefix-based invalidation. Mutually exclusive with <see cref="Key"/> and <see cref="FlushAll"/>.</summary>
    public string? Prefix { get; init; }

    /// <summary>When true, flush all cache entries. Mutually exclusive with <see cref="Key"/> and <see cref="Prefix"/>.</summary>
    public bool FlushAll { get; init; }
}
