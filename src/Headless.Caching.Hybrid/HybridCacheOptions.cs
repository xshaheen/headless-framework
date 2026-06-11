// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Configuration options for the hybrid cache.</summary>
[PublicAPI]
public sealed class HybridCacheOptions : CacheOptions
{
    /// <summary>
    /// Default local (L1) cache TTL. If null, uses the same expiration as the distributed (L2) cache.
    /// </summary>
    /// <remarks>
    /// Setting a shorter TTL for L1 ensures local caches refresh more frequently from L2,
    /// reducing stale data in multi-instance deployments even if invalidation messages are missed.
    /// </remarks>
    public TimeSpan? DefaultLocalExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Unique identifier for this cache instance. Used to filter out self-originated invalidation messages.
    /// Auto-generated if null.
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Optional name of a keyed <see cref="ICache"/> registration to use as the local (L1) tier. The named
    /// cache must implement <see cref="IInMemoryCache"/> (register it with
    /// <c>setup.AddNamed(name, i => i.UseInMemory(…))</c>);
    /// otherwise resolution fails with <see cref="InvalidOperationException"/>. When <see langword="null"/>
    /// (the default) the hybrid cache uses the default <see cref="IInMemoryCache"/> registration.
    /// </summary>
    public string? LocalCacheName { get; set; }

    /// <summary>
    /// Optional name of a keyed <see cref="ICache"/> registration to use as the distributed (L2) tier. The
    /// named cache must implement <see cref="IRemoteCache"/> (register it with
    /// <c>setup.AddNamed(name, i => i.UseRedis(…))</c>);
    /// otherwise resolution fails with <see cref="InvalidOperationException"/>. When <see langword="null"/>
    /// (the default) the hybrid cache uses the default <see cref="IRemoteCache"/> registration.
    /// </summary>
    public string? RemoteCacheName { get; set; }

    /// <summary>
    /// Enables opt-in auto-recovery: when the distributed (L2) tier or the invalidation backplane has a
    /// transient outage, failed single-key L2 writes/removes and failed invalidation publishes are queued and
    /// replayed once the dependency recovers instead of surfacing every failure to callers. Default is
    /// <see langword="false"/> (today's behavior: scalar L2 failures propagate; factory-path L2 failures are
    /// logged and dropped).
    /// </summary>
    public bool EnableAutoRecovery { get; set; }

    /// <summary>
    /// Maximum number of pending recovery items (one per cache key). On overflow the item with the earliest
    /// expiry is evicted to admit the new one. Only used when <see cref="EnableAutoRecovery"/> is enabled.
    /// </summary>
    public int AutoRecoveryMaxItems { get; set; } = 128;

    /// <summary>
    /// Maximum number of failed replay attempts before a pending recovery item is dropped with a warning.
    /// Only used when <see cref="EnableAutoRecovery"/> is enabled.
    /// </summary>
    public int AutoRecoveryMaxRetries { get; set; } = 8;

    /// <summary>
    /// Cadence of the recovery processing loop and the back-off barrier applied after a failed replay.
    /// Only used when <see cref="EnableAutoRecovery"/> is enabled.
    /// </summary>
    public TimeSpan AutoRecoveryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
