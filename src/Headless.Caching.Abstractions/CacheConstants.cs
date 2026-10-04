// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Defines keyed dependency injection constants for caching packages. The three role keys are reserved: each provider setup
/// registers its cache under the matching role key (<c>UseInMemory</c> maps to <see cref="MemoryCacheProvider"/>,
/// <c>UseRedis</c> maps to <see cref="RemoteCacheProvider"/>, and <c>UseHybrid</c> maps to <see cref="HybridCacheProvider"/>).
/// The keys use the <c>Headless.Caching:</c> namespace prefix to avoid collisions with consumer-owned keyed
/// services. Named cache instances must not use a reserved name.
/// </summary>
[PublicAPI]
public static class CacheConstants
{
    /// <summary>Represents the keyed dependency injection service key under which the L2 remote cache is registered by <c>UseRedis</c>.</summary>
    public const string RemoteCacheProvider = "Headless.Caching:Remote";

    /// <summary>Represents the keyed dependency injection service key under which the L1 in-memory cache is registered by <c>UseInMemory</c>.</summary>
    public const string MemoryCacheProvider = "Headless.Caching:Memory";

    /// <summary>Represents the keyed dependency injection service key under which the hybrid two-tier cache is registered by <c>UseHybrid</c>.</summary>
    public const string HybridCacheProvider = "Headless.Caching:Hybrid";

    /// <summary>
    /// Indicates whether <paramref name="name"/> is reserved for caching role registrations.
    /// </summary>
    /// <param name="name">The candidate cache instance name.</param>
    /// <returns><see langword="true"/> when the name begins with the reserved prefix; otherwise, <see langword="false"/>.</returns>
    public static bool IsReservedProviderKey(string name)
    {
        return name.StartsWith("Headless.Caching:", StringComparison.Ordinal);
    }
}
