// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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
    /// Gets the cache entry duration. A positive value sets the entry's lifetime; a non-positive value (zero or
    /// negative — for example a BCL absolute expiration already in the past) is treated as "expire immediately":
    /// the write becomes an immediate eviction across every provider rather than throwing.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the optional idle window for sliding expiration. When set, value-returning reads push the logical
    /// expiration to <c>min(now + SlidingExpiration, createdAt + Duration)</c>; values greater than or equal to
    /// <see cref="Duration"/> therefore behave like a fixed duration. Sliding expiration and fail-safe are not
    /// supported together in this version and are rejected by the factory coordinator.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; init; }

    /// <summary>
    /// Gets the maximum random duration added to <see cref="Duration"/> on each write to desynchronize the expiry
    /// of entries created together (anti-stampede): without it, a batch of entries written in the same burst all
    /// expire at the same instant and trigger a synchronized factory stampede on the next read wave. The actual
    /// offset is sampled uniformly in <c>[0, JitterMaxDuration)</c> per write and is applied to the entry's
    /// logical, physical, and eager spans alike, so it never breaks the <c>physical &gt;= logical</c> invariant the
    /// engine relies on. Defaults to <see cref="TimeSpan.Zero"/> (no jitter). Must be zero or positive.
    /// </summary>
    public TimeSpan JitterMaxDuration { get; init; }

    /// <summary>
    /// Gets the optional eager-refresh point as a fraction of <see cref="Duration"/>, exclusive between 0 and 1.
    /// When set, a fresh `GetOrAddAsync` hit past <c>createdAt + Duration × threshold</c> returns the cached value
    /// immediately and starts a non-blocking background refresh, deduplicated per key. Eager refresh and sliding
    /// expiration are not supported together and are rejected by the factory coordinator.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown at the start of a factory-backed operation when the value is set and is not strictly greater than
    /// <c>0</c> or not strictly less than <c>1</c> — i.e. when it equals <c>0</c>, equals <c>1</c>, or falls
    /// outside the open interval <c>(0, 1)</c>.
    /// </exception>
    public float? EagerRefreshThreshold { get; init; }

    /// <summary>
    /// Gets a value indicating whether factory-backed cache operations can serve the physically-retained
    /// stale value when the factory fails.
    /// </summary>
    public bool IsFailSafeEnabled { get; init; }

    /// <summary>
    /// Gets the maximum duration from entry creation for which a stale value can be served when fail-safe
    /// activates. The coordinator applies <c>max(Duration, FailSafeMaxDuration)</c> so physical retention
    /// never ends before logical freshness.
    /// </summary>
    public TimeSpan FailSafeMaxDuration { get; init; } = DefaultFailSafeMaxDuration;

    /// <summary>
    /// Gets the throttle window applied after fail-safe activates. The coordinator re-stamps the stale
    /// reserve with a fresh logical lifetime of this duration, clamped to the entry's remaining physical
    /// lifetime (<c>min(now + FailSafeThrottleDuration, physicalExpiresAt)</c>). Within that window reads
    /// are served the last-known-good value as fresh, so the failing factory is not re-invoked until the
    /// window lapses. The clamp ensures the throttle never extends physical retention.
    /// </summary>
    public TimeSpan FailSafeThrottleDuration { get; init; } = DefaultFailSafeThrottleDuration;

    /// <summary>
    /// Gets how long a factory-backed read waits before returning a stale value and letting the factory
    /// continue in the background. Applies only when fail-safe is enabled and a stale reserve exists.
    /// </summary>
    public TimeSpan FactorySoftTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets the absolute factory timeout. When this timeout fires, the coordinator cancels the factory and
    /// either serves a stale value or throws <see cref="CacheFactoryTimeoutException"/>.
    /// </summary>
    public TimeSpan FactoryHardTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets the runaway guard for a detached background factory after a soft timeout. Defaults to
    /// <see cref="Timeout.InfiniteTimeSpan"/> (no ceiling): a detached factory runs to completion, matching
    /// the behavior of comparable caches. Provide a finite, positive value to bound how long a detached
    /// factory may hold the per-key lock; when the ceiling fires, the coordinator cancels the internal token,
    /// releases the lock, and best-effort re-stamps the stale reserve. Must be finite when
    /// <see cref="IsFailSafeEnabled"/> is set together with a finite <see cref="FactorySoftTimeout"/>: that is
    /// the only combination that detaches the factory, so an infinite ceiling would let a hung factory hold the
    /// per-key lock indefinitely. Validation rejects that combination.
    /// </summary>
    public TimeSpan BackgroundFactoryCeiling { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets how long a factory-backed read waits to acquire the per-key factory lock when no stale reserve is
    /// available to serve. Defaults to <see cref="Timeout.InfiniteTimeSpan"/> (wait until the in-flight factory
    /// releases the lock, matching the behavior of comparable caches). Provide a finite, positive value so a caller
    /// that cannot acquire the lock in time degrades to a miss (<c>CacheValue&lt;T&gt;.NoValue</c>) instead of
    /// blocking, bounding tail latency when an in-flight factory is slow and no fail-safe reserve exists. When a
    /// stale reserve does exist and <see cref="FactorySoftTimeout"/> is finite, that soft timeout governs the wait
    /// instead and the caller is served the stale value on elapse.
    /// </summary>
    public TimeSpan LockTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets a value indicating whether the factory for this entry is additionally guarded by a distributed lock so
    /// that, across nodes sharing the same store, only one node runs the factory for the key while the others
    /// coordinate through the lock and re-check the shared store (multi-node stampede protection). Off by default
    /// and adds zero cost when disabled. Opt-in per entry; requires a registered
    /// <c>ICacheFactoryLockProvider</c> — enabling it without one fails the factory-backed read with an
    /// <see cref="InvalidOperationException"/> naming the adapter package (<c>Headless.Caching.DistributedLocks</c>)
    /// rather than silently degrading to single-node behavior.
    /// </summary>
    public bool UseDistributedFactoryLock { get; init; }

    /// <summary>
    /// Gets the optional invalidation tags persisted with the entry. Tagged entries can later be removed in one
    /// call with <see cref="ICache.RemoveByTagAsync"/>. When set on a factory-backed read, call-provided tags win
    /// over the tags carried by an existing entry; when <see langword="null"/>, an existing entry's tags are
    /// carried forward unchanged. Each tag must be non-empty, and both the tag count and each tag's UTF-8 byte
    /// length must fit in an unsigned 16-bit value (provider envelope limits); violations are rejected with an
    /// <see cref="ArgumentException"/> before anything is written.
    /// </summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>
    /// Gets a value indicating whether the value must not be written to the L1 (memory) tier. Hybrid-relevant:
    /// single-tier providers (the in-memory cache has only L1, Redis only L2) ignore it. When set on a
    /// factory-backed read, the freshly-produced value is fanned to L2 only; L1 stays untouched (useful for a
    /// large value that should not occupy process memory). Defaults to <see langword="false"/>.
    /// </summary>
    public bool SkipMemoryCacheWrite { get; init; }

    /// <summary>
    /// Gets a value indicating whether the value must not be written to the L2 (distributed) tier. Hybrid-relevant:
    /// single-tier providers (the in-memory cache has only L1, Redis only L2) ignore it. When set on a
    /// factory-backed read, the freshly-produced value is written to L1 only; the L2 mirror and its peer
    /// invalidation publish are skipped (useful for a node-local value that should not be shared). Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool SkipDistributedCacheWrite { get; init; }

    /// <summary>
    /// Gets a value indicating whether <c>GetOrAddAsync</c> must force-refresh: bypass the cached read on both
    /// tiers and always run the factory, then store the result subject to <see cref="SkipMemoryCacheWrite"/> and
    /// <see cref="SkipDistributedCacheWrite"/>. Hybrid-relevant: single-tier providers (the in-memory cache has
    /// only L1, Redis only L2) ignore it. Because no cached entry is read, no stale fail-safe reserve is loaded:
    /// a factory failure has nothing to fall back to and propagates even when <see cref="IsFailSafeEnabled"/> is
    /// set. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// This is the coarse both-tier form. <see cref="SkipMemoryCacheRead"/> and <see cref="SkipDistributedCacheRead"/>
    /// are the granular per-tier form: setting both of them is equivalent to <see cref="SkipCacheRead"/> (no tier is
    /// read, so the factory always runs with no reserve to fall back to). When <see cref="SkipCacheRead"/> is set it
    /// wins outright — the coordinator issues no store read at all, so the two per-tier flags have no additional
    /// effect.
    /// </remarks>
    public bool SkipCacheRead { get; init; }

    /// <summary>
    /// Gets a value indicating whether the L1 (memory) tier must not be read on a factory-backed
    /// <c>GetOrAddAsync</c>, so the read is served from (or refreshed against) the L2 (distributed) tier instead.
    /// This is the granular, per-tier counterpart to <see cref="SkipCacheRead"/> (which skips both reads) and
    /// mirrors FusionCache's <c>SkipMemoryCacheRead</c>. Hybrid-relevant: single-tier providers (the in-memory cache
    /// has only L1, Redis only L2) ignore it — there is only one tier, so nothing is skipped. Unlike
    /// <see cref="SkipCacheRead"/>, a value read from L2 is still honored, so an existing L2 reserve remains
    /// available for fail-safe. Setting this together with <see cref="SkipDistributedCacheRead"/> reads neither tier
    /// and is equivalent to <see cref="SkipCacheRead"/>. Defaults to <see langword="false"/>.
    /// </summary>
    public bool SkipMemoryCacheRead { get; init; }

    /// <summary>
    /// Gets a value indicating whether the L2 (distributed) tier must not be read on a factory-backed
    /// <c>GetOrAddAsync</c>, so the read is served from the L1 (memory) tier when present and otherwise falls through
    /// to the factory without an L2 round-trip. This is the granular, per-tier counterpart to
    /// <see cref="SkipCacheRead"/> (which skips both reads) and mirrors FusionCache's <c>SkipDistributedCacheRead</c>.
    /// Hybrid-relevant: single-tier providers (the in-memory cache has only L1, Redis only L2) ignore it — there is
    /// only one tier, so nothing is skipped. A value or reserve found in L1 is still honored (including for
    /// fail-safe). Setting this together with <see cref="SkipMemoryCacheRead"/> reads neither tier and is equivalent
    /// to <see cref="SkipCacheRead"/>. Defaults to <see langword="false"/>.
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
