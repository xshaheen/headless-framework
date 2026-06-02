// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Options that describe a cache entry created by a factory-backed cache operation.
/// </summary>
/// <remarks>
/// This type is the extension point for future factory-backed cache behaviors such as fail-safe,
/// factory timeouts, refresh, and tagging. This slice only activates <see cref="Duration"/>.
/// </remarks>
[PublicAPI]
public sealed class CacheEntryOptions
{
    /// <summary>Gets or sets the cache entry duration.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static CacheEntryOptions FromTimeSpan(TimeSpan duration) => new() { Duration = duration };

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static implicit operator CacheEntryOptions(TimeSpan duration) => FromTimeSpan(duration);
}
