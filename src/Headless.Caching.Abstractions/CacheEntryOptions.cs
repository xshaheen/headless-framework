// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Options that describe a cache entry created by a factory-backed cache operation.
/// </summary>
/// <remarks>
/// This type is the extension point for factory-backed cache behaviors. <see cref="Duration"/>
/// controls logical freshness. When fail-safe is enabled, the factory coordinator keeps the entry
/// physically resident for <c>max(Duration, FailSafeMaxDuration)</c> so <c>GetOrAddAsync</c> can serve
/// the last-known-good value after a factory failure or timeout. When <see cref="SlidingExpiration"/> is set,
/// <see cref="Duration"/> remains the absolute ceiling while successful value reads re-arm the idle deadline.
/// </remarks>
[PublicAPI]
public readonly record struct CacheEntryOptions
{
    /// <summary>Default maximum duration that a fail-safe reserve can be served.</summary>
    public static readonly TimeSpan DefaultFailSafeMaxDuration = TimeSpan.FromDays(1);

    /// <summary>Default duration used to throttle factory retries after fail-safe activates.</summary>
    public static readonly TimeSpan DefaultFailSafeThrottleDuration = TimeSpan.FromSeconds(30);

    /// <summary>Initializes a new instance of the <see cref="CacheEntryOptions"/> struct.</summary>
    public CacheEntryOptions()
    {
        FailSafeMaxDuration = DefaultFailSafeMaxDuration;
        FailSafeThrottleDuration = DefaultFailSafeThrottleDuration;
        FactorySoftTimeout = Timeout.InfiniteTimeSpan;
        FactoryHardTimeout = Timeout.InfiniteTimeSpan;
        BackgroundFactoryCeiling = Timeout.InfiniteTimeSpan;
        LockTimeout = Timeout.InfiniteTimeSpan;
    }

    /// <summary>
    /// Gets the cache entry duration. A positive value sets the lifetime of the entry. A non-positive value
    /// such as zero or a negative interval causes immediate expiration across providers rather than throwing.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the optional idle window for sliding expiration. When set, reads that return values extend the logical
    /// expiration to the earlier of current time plus <see cref="SlidingExpiration"/> or creation time plus <see cref="Duration"/>.
    /// Sliding expiration and fail-safe cannot be combined and are rejected by the factory coordinator.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; init; }

    /// <summary>
    /// Gets the maximum random duration added to <see cref="Duration"/> on each write to avoid synchronized expiry
    /// across entries written in the same burst. The offset is sampled uniformly between zero and <see cref="JitterMaxDuration"/>
    /// and applies to logical, physical, and eager spans. Defaults to <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan JitterMaxDuration { get; init; }

    /// <summary>
    /// Gets the optional eager-refresh point as an exclusive fraction of <see cref="Duration"/> between 0 and 1.
    /// When set, a cache hit after the threshold triggers a non-blocking background refresh deduplicated per key.
    /// Eager refresh and sliding expiration cannot be combined and are rejected by the factory coordinator.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is set and falls outside the open interval (0, 1).
    /// </exception>
    public float? EagerRefreshThreshold { get; init; }

    /// <summary>
    /// Gets a value indicating whether factory-backed cache operations can serve the physically retained
    /// stale value when the factory fails.
    /// </summary>
    public bool IsFailSafeEnabled { get; init; }

    /// <summary>
    /// Gets the maximum duration from entry creation for which a stale value can be served when fail-safe
    /// activates. The coordinator applies the greater of <see cref="Duration"/> and <see cref="FailSafeMaxDuration"/>.
    /// </summary>
    public TimeSpan FailSafeMaxDuration { get; init; } = DefaultFailSafeMaxDuration;

    /// <summary>
    /// Gets the throttle window applied after fail-safe activates. The coordinator restamps the stale
    /// reserve with a fresh logical lifetime of this duration, clamped to the remaining physical
    /// lifetime of the entry. Reads within that window return the last-known-good value as fresh.
    /// </summary>
    public TimeSpan FailSafeThrottleDuration { get; init; } = DefaultFailSafeThrottleDuration;

    /// <summary>
    /// Gets how long a factory-backed read waits before returning a stale value and running the factory
    /// in the background. Applies only when fail-safe is enabled and a stale reserve exists.
    /// </summary>
    public TimeSpan FactorySoftTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets the absolute factory timeout. When this timeout expires, the coordinator cancels the factory and
    /// either serves a stale value or throws <see cref="CacheFactoryTimeoutException"/>.
    /// </summary>
    public TimeSpan FactoryHardTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets the runaway guard for a detached background factory after a soft timeout. Defaults to
    /// <see cref="Timeout.InfiniteTimeSpan"/>, which allows the detached factory to run to completion.
    /// Provide a finite, positive value to bound how long a detached factory can hold the per-key lock.
    /// Must be finite when <see cref="IsFailSafeEnabled"/> is set with a finite <see cref="FactorySoftTimeout"/>.
    /// </summary>
    public TimeSpan BackgroundFactoryCeiling { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets how long a factory-backed read waits to acquire the per-key factory lock when no stale reserve is
    /// available. Defaults to <see cref="Timeout.InfiniteTimeSpan"/>. Provide a finite, positive value so a caller
    /// that cannot acquire the lock returns <see cref="CacheValue{T}.NoValue"/> instead of blocking. When a stale
    /// reserve exists and <see cref="FactorySoftTimeout"/> is finite, that soft timeout governs the wait.
    /// </summary>
    public TimeSpan LockTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets a value indicating whether the factory for this entry is guarded by a distributed lock across nodes
    /// sharing the same store. Defaults to <see langword="false"/>. When enabled, requires a registered
    /// <c>ICacheFactoryLockProvider</c> from the <c>Headless.Caching.DistributedLocks</c> package.
    /// </summary>
    public bool UseDistributedFactoryLock { get; init; }

    /// <summary>
    /// Gets the optional invalidation tags persisted with the entry. Tagged entries can be removed in one
    /// call through <see cref="ICache.RemoveByTagAsync"/>. When set on a factory-backed read, provided tags override
    /// tags carried by an existing entry. When <see langword="null"/>, existing tags are preserved.
    /// </summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>
    /// Gets a value indicating whether the value must not be written to the L1 memory tier in hybrid caching.
    /// When set on a factory-backed read, the generated value is written to L2 only. Defaults to <see langword="false"/>.
    /// </summary>
    public bool SkipMemoryCacheWrite { get; init; }

    /// <summary>
    /// Gets a value indicating whether the value must not be written to the L2 distributed tier in hybrid caching.
    /// When set on a factory-backed read, the generated value is written to L1 only, skipping L2 and peer invalidation.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool SkipDistributedCacheWrite { get; init; }

    /// <summary>
    /// Gets a value indicating whether cache reads must be bypassed on both tiers during <c>GetOrAddAsync</c>.
    /// When <see langword="true"/>, the factory always executes, and no stale reserve is loaded.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Setting this property overrides <see cref="SkipMemoryCacheRead"/> and <see cref="SkipDistributedCacheRead"/>.
    /// </remarks>
    public bool SkipCacheRead { get; init; }

    /// <summary>
    /// Gets a value indicating whether the L1 memory tier must not be read during a factory-backed <c>GetOrAddAsync</c> call.
    /// When <see langword="true"/>, the read is served from or refreshed against L2 instead.
    /// Setting both this and <see cref="SkipDistributedCacheRead"/> is equivalent to setting <see cref="SkipCacheRead"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool SkipMemoryCacheRead { get; init; }

    /// <summary>
    /// Gets a value indicating whether the L2 distributed tier must not be read during a factory-backed <c>GetOrAddAsync</c> call.
    /// When <see langword="true"/>, the read is served from L1 or falls through to the factory without an L2 call.
    /// Setting both this and <see cref="SkipMemoryCacheRead"/> is equivalent to setting <see cref="SkipCacheRead"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool SkipDistributedCacheRead { get; init; }

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static CacheEntryOptions FromTimeSpan(TimeSpan duration)
    {
        return new() { Duration = duration };
    }

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static implicit operator CacheEntryOptions(TimeSpan duration) => FromTimeSpan(duration);
}
