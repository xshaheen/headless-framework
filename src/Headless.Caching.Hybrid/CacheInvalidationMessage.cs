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

    /// <summary>Tag for tag-based invalidation (<see cref="ICache.RemoveByTagAsync"/>). Mutually exclusive with <see cref="Key"/>, <see cref="Keys"/>, <see cref="Prefix"/>, and <see cref="FlushAll"/>.</summary>
    public string? Tag { get; init; }

    /// <summary>When true, flush all cache entries. Mutually exclusive with <see cref="Key"/> and <see cref="Prefix"/>.</summary>
    public bool FlushAll { get; init; }

    /// <summary>
    /// UTC timestamp at which the originating instance published this invalidation. Stamped automatically by
    /// <see cref="HybridCache"/> on publish. Receivers use it to resolve conflicts with auto-recovery items
    /// queued locally: a queued write older than an incoming invalidation is dropped so replaying it cannot
    /// resurrect stale data.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }
}
