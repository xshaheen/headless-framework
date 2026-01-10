// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Caching.Memory;

namespace Framework.Caching;

/// <summary>
/// Options for a single cache entry in <see cref="IHybridCache"/>.
/// These options override the global <see cref="HybridCacheOptions"/> for a specific operation.
/// </summary>
[PublicAPI]
public sealed record HybridCacheEntryOptions
{
    /// <summary>
    /// The logical duration of the cache entry. After this time, the entry is considered stale
    /// and will be refreshed on next access (or eagerly if <see cref="EagerRefreshThreshold"/> is set).
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// When fail-safe is enabled, this is the maximum duration to keep stale values
    /// for fallback scenarios when the factory fails.
    /// </summary>
    public TimeSpan? FailSafeMaxDuration { get; init; }

    /// <summary>
    /// Maximum time to wait for the factory to complete. If exceeded, a stale value
    /// may be returned (if fail-safe is enabled) or the operation may fail.
    /// </summary>
    public TimeSpan? FactoryTimeout { get; init; }

    /// <summary>
    /// When <see langword="true"/>, returns stale cached values when the factory fails.
    /// This provides resilience against temporary failures.
    /// </summary>
    public bool? EnableFailSafe { get; init; }

    /// <summary>
    /// A value between 0.0 and 1.0 indicating when to trigger eager background refresh.
    /// For example, 0.9 means refresh when 90% of the duration has passed.
    /// This helps keep the cache warm and reduces latency spikes.
    /// </summary>
    public float? EagerRefreshThreshold { get; init; }

    /// <summary>
    /// The priority of the cache entry for eviction purposes.
    /// Higher priority entries are less likely to be evicted under memory pressure.
    /// </summary>
    public CacheItemPriority Priority { get; init; } = CacheItemPriority.Normal;
}
