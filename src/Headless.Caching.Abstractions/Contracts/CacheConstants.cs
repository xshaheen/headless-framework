// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Keyed-DI constants for the caching packages. The three role keys below are reserved: every provider setup
/// registers its cache under the matching role key (<c>UseInMemory</c> → <see cref="MemoryCacheProvider"/>,
/// <c>UseRedis</c> → <see cref="RemoteCacheProvider"/>, <c>UseHybrid</c> → <see cref="HybridCacheProvider"/>),
/// so named cache instances must not use them — <c>setup.AddNamed(…)</c> rejects them with
/// <see cref="ArgumentException"/>.
/// </summary>
public static class CacheConstants
{
    public const string RemoteCacheProvider = "remote";
    public const string MemoryCacheProvider = "memory";
    public const string HybridCacheProvider = "hybrid";

    /// <summary>Indicates whether <paramref name="name"/> is one of the reserved role keys.</summary>
    /// <param name="name">The candidate cache instance name.</param>
    /// <returns><see langword="true"/> when the name is reserved.</returns>
    public static bool IsReservedProviderKey(string name)
    {
        return name is RemoteCacheProvider or MemoryCacheProvider or HybridCacheProvider;
    }
}
