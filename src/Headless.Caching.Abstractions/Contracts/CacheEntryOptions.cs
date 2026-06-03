// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA2225 // Intentional: the implicit TimeSpan conversion is the compatibility shorthand; no extra public factory.
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
public readonly record struct CacheEntryOptions
{
    /// <summary>
    /// Gets the cache entry duration. Must be a positive value; zero or negative durations are
    /// rejected with an <see cref="ArgumentOutOfRangeException"/> when the entry is created.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Creates cache entry options from a cache duration.</summary>
    /// <param name="duration">The cache entry duration.</param>
    public static implicit operator CacheEntryOptions(TimeSpan duration) => new() { Duration = duration };
}
