// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

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
    /// Enables opt-in fire-and-forget distributed (L2) writes on additive write paths (the GetOrAdd factory
    /// write-through, <see cref="ICache.UpsertAsync{T}"/>, and <see cref="ICache.UpsertAllAsync{T}"/>). When
    /// <see langword="true"/>, the caller writes L1 synchronously and returns without awaiting the L2 write or
    /// its invalidation publish; both run detached in the background. The caller's result never depends on the
    /// L2 outcome, so this trades cross-instance write latency for tail-latency: a slow or briefly unavailable
    /// L2 tier no longer blocks the caller. A failed background write is routed to the auto-recovery queue when
    /// <see cref="EnableAutoRecovery"/> is on, otherwise it is logged and dropped (best-effort, matching the
    /// fire-and-forget contract). Default is <see langword="false"/> (today's behavior: L2 writes are awaited).
    /// </summary>
    /// <remarks>
    /// Only paths whose return value is independent of L2 are backgrounded. Removes, conditional/atomic ops
    /// (TryInsert, TryReplace, Increment, SetIfHigher/Lower, CAS), set/list ops, and all reads stay synchronous
    /// because their result depends on the L2 response or is correctness-sensitive.
    /// </remarks>
    public bool AllowBackgroundDistributedCacheOperations { get; set; }

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

internal sealed class HybridCacheOptionsValidator : AbstractValidator<HybridCacheOptions>
{
    public HybridCacheOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotNull().WithMessage("KeyPrefix cannot be null");

        RuleFor(x => x.DefaultLocalExpiration)
            .Must(x => x is null || x.Value > TimeSpan.Zero)
            .WithMessage("DefaultLocalExpiration must be positive if set");

        RuleFor(x => x.LocalCacheName)
            .Must(x => x is null || !string.IsNullOrWhiteSpace(x))
            .WithMessage("LocalCacheName must be non-empty if set");

        RuleFor(x => x.RemoteCacheName)
            .Must(x => x is null || !string.IsNullOrWhiteSpace(x))
            .WithMessage("RemoteCacheName must be non-empty if set");

        RuleFor(x => x.AutoRecoveryMaxItems).GreaterThan(0).WithMessage("AutoRecoveryMaxItems must be > 0");

        RuleFor(x => x.AutoRecoveryMaxRetries).GreaterThan(0).WithMessage("AutoRecoveryMaxRetries must be > 0");

        RuleFor(x => x.AutoRecoveryDelay)
            .Must(x => x > TimeSpan.Zero)
            .WithMessage("AutoRecoveryDelay must be positive");
    }
}
