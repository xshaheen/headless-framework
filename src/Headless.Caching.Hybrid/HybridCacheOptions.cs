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
    /// Maximum time to wait for a distributed (L2) read when Hybrid can degrade to a local fallback or a miss.
    /// Defaults to <see cref="Timeout.InfiniteTimeSpan"/> (disabled).
    /// </summary>
    public TimeSpan DistributedCacheSoftTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Maximum time to wait for a distributed (L2) read when no local fallback exists. Defaults to
    /// <see cref="Timeout.InfiniteTimeSpan"/> (disabled).
    /// </summary>
    public TimeSpan DistributedCacheHardTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Duration for which Hybrid temporarily skips distributed (L2) operations after a non-cancellation L2
    /// failure. Defaults to <see cref="TimeSpan.Zero"/> (disabled).
    /// </summary>
    public TimeSpan DistributedCacheCircuitBreakerDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// When <see langword="true"/>, a non-cancellation distributed (L2) read or factory-write failure is
    /// re-thrown to the caller instead of being logged and degraded. Defaults to <see langword="false"/>
    /// (today's behavior: L2 faults degrade — reads fall back to L1 or a miss, factory writes fall back to L1
    /// with optional recovery queueing).
    /// </summary>
    /// <remarks>
    /// Scope: governs the central L2 read chokepoint (direct <see cref="ICache.GetAsync{T}"/>,
    /// <c>GetAllAsync</c>, <c>ExistsAsync</c>, <c>GetExpirationAsync</c>, <c>GetSetAsync</c>) and the
    /// factory/<c>UpsertEntryAsync</c> store-write chokepoint. It does NOT change three things: a
    /// timeout or open circuit still degrades (no exception to re-throw); a sliding re-arm hiccup stays
    /// best-effort (never fails a successful read); and a <c>GetOrAddAsync</c> store-read
    /// fault still falls through to the factory (cache-aside semantics — the read is treated as a miss).
    /// The scalar additive-write degraded paths remain governed by <see cref="EnableAutoRecovery"/> /
    /// <see cref="DistributedCacheCircuitBreakerDuration"/>; with neither configured they already propagate.
    /// </remarks>
    public bool ReThrowDistributedCacheExceptions { get; set; }

    /// <summary>
    /// When <see langword="true"/>, a non-cancellation failure publishing a cache-invalidation message to the
    /// backplane is re-thrown to the caller after the failure is logged (and, when
    /// <see cref="EnableAutoRecovery"/> is on, queued for replay). Defaults to <see langword="false"/>
    /// (today's behavior: publish failures are non-fatal — peers may hold stale L1 until their TTL elapses).
    /// </summary>
    /// <remarks>
    /// A publish runs on both synchronous write paths (where the re-throw surfaces to the caller) and detached
    /// background paths (where it is observed and logged by the fire-and-forget fault net). Enabling this trades
    /// the eventual-consistency guarantee for fail-loud visibility into backplane outages.
    /// </remarks>
    public bool ReThrowBackplaneExceptions { get; set; }

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

        RuleFor(x => x.DistributedCacheSoftTimeout)
            .Must(_IsValidTimeout)
            .WithMessage("DistributedCacheSoftTimeout must be positive or Timeout.InfiniteTimeSpan");

        RuleFor(x => x.DistributedCacheHardTimeout)
            .Must(_IsValidTimeout)
            .WithMessage("DistributedCacheHardTimeout must be positive or Timeout.InfiniteTimeSpan");

        RuleFor(x => x.DistributedCacheHardTimeout)
            .GreaterThan(x => x.DistributedCacheSoftTimeout)
            .When(x =>
                x.DistributedCacheSoftTimeout != Timeout.InfiniteTimeSpan
                && x.DistributedCacheHardTimeout != Timeout.InfiniteTimeSpan
            )
            .WithMessage("DistributedCacheHardTimeout must be greater than DistributedCacheSoftTimeout");

        RuleFor(x => x.DistributedCacheCircuitBreakerDuration)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .WithMessage("DistributedCacheCircuitBreakerDuration must be zero or positive");
    }

    private static bool _IsValidTimeout(TimeSpan timeout)
    {
        return timeout == Timeout.InfiniteTimeSpan || timeout > TimeSpan.Zero;
    }
}
