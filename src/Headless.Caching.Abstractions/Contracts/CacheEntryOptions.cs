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
    /// Gets the cache entry duration. Must be a positive value; zero or negative durations are
    /// rejected with an <see cref="ArgumentOutOfRangeException"/> when the entry is created.
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
    /// Gets the optional eager-refresh point as a fraction of <see cref="Duration"/>, exclusive between 0 and 1.
    /// When set, a fresh `GetOrAddAsync` hit past <c>createdAt + Duration × threshold</c> returns the cached value
    /// immediately and starts a non-blocking background refresh, deduplicated per key. Eager refresh and sliding
    /// expiration are not supported together and are rejected by the factory coordinator.
    /// </summary>
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
    /// releases the lock, and best-effort re-stamps the stale reserve.
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
    /// Gets the optional invalidation tags persisted with the entry. Tagged entries can later be removed in one
    /// call with <see cref="ICache.RemoveByTagAsync"/>. When set on a factory-backed read, call-provided tags win
    /// over the tags carried by an existing entry; when <see langword="null"/>, an existing entry's tags are
    /// carried forward unchanged. Each tag must be non-empty, and both the tag count and each tag's UTF-8 byte
    /// length must fit in an unsigned 16-bit value (provider envelope limits); violations are rejected with an
    /// <see cref="ArgumentException"/> before anything is written.
    /// </summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static CacheEntryOptions FromTimeSpan(TimeSpan duration) => new() { Duration = duration };

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static implicit operator CacheEntryOptions(TimeSpan duration) => FromTimeSpan(duration);
}
