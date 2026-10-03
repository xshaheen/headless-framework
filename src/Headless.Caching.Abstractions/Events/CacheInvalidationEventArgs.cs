// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Arguments for a hybrid invalidation propagation, carrying its kind, direction, and (for tag kind) the tag.</summary>
[PublicAPI]
public sealed class CacheInvalidationEventArgs(
    string cacheName,
    CacheTier tier,
    CacheInvalidationKind kind,
    CacheInvalidationDirection direction,
    string? tag = null
) : CacheEventArgs(cacheName, tier)
{
    /// <summary>The invalidation kind.</summary>
    public CacheInvalidationKind Kind { get; } = kind;

    /// <summary>Whether this instance published or received the invalidation.</summary>
    public CacheInvalidationDirection Direction { get; } = direction;

    /// <summary>The tag for a <see cref="CacheInvalidationKind.Tag"/> invalidation; <see langword="null"/> for clear/flush.</summary>
    public string? Tag { get; } = tag;
}
