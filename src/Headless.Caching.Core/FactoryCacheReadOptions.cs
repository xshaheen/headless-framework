// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Per-call read-tier controls threaded into <see cref="IFactoryCacheStore"/> reads so a multi-tier store (the
/// hybrid cache) can skip reading an individual tier while still reading the other. The default value
/// (<see cref="None"/>) reads every tier, preserving the historical read-both behavior.
/// </summary>
/// <remarks>
/// Single-tier providers (the in-memory cache has only L1, Redis only L2) accept and ignore these flags — there is
/// only one tier, so nothing can be skipped. The hybrid cache maps them from
/// <see cref="CacheEntryOptions.SkipMemoryCacheRead"/> / <see cref="CacheEntryOptions.SkipDistributedCacheRead"/>:
/// <see cref="SkipMemoryRead"/> bypasses L1 and reads L2; <see cref="SkipDistributedRead"/> bypasses L2 and serves
/// from L1 (or a factory miss); both set reads neither tier (a miss, equivalent to
/// <see cref="CacheEntryOptions.SkipCacheRead"/>).
/// </remarks>
[PublicAPI]
public readonly record struct FactoryCacheReadOptions
{
    /// <summary>Gets a value indicating whether the memory (L1) tier must not be read.</summary>
    public bool SkipMemoryRead { get; init; }

    /// <summary>Gets a value indicating whether the distributed (L2) tier must not be read.</summary>
    public bool SkipDistributedRead { get; init; }

    /// <summary>Gets the default value that reads every tier (the historical read-both behavior).</summary>
    public static FactoryCacheReadOptions None => default;

    /// <summary>
    /// Projects the per-tier read-skip flags carried on <paramref name="options"/> onto a
    /// <see cref="FactoryCacheReadOptions"/> value.
    /// </summary>
    /// <param name="options">The cache entry options whose read-skip flags are read.</param>
    [Pure]
    public static FactoryCacheReadOptions FromEntryOptions(CacheEntryOptions options) =>
        new() { SkipMemoryRead = options.SkipMemoryCacheRead, SkipDistributedRead = options.SkipDistributedCacheRead };
}
