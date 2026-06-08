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
/// the last-known-good value after a factory failure.
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
    }

    /// <summary>
    /// Gets the cache entry duration. Must be a positive value; zero or negative durations are
    /// rejected with an <see cref="ArgumentOutOfRangeException"/> when the entry is created.
    /// </summary>
    public TimeSpan Duration { get; init; }

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
    /// Gets the duration used to throttle factory retries after fail-safe activates. The coordinator
    /// clamps this value to the remaining physical lifetime and never extends physical retention.
    /// </summary>
    public TimeSpan FailSafeThrottleDuration { get; init; } = DefaultFailSafeThrottleDuration;

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static CacheEntryOptions FromTimeSpan(TimeSpan duration) => new() { Duration = duration };

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static implicit operator CacheEntryOptions(TimeSpan duration) => FromTimeSpan(duration);
}
