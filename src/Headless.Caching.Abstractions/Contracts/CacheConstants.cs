// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Keyed-DI constants for the caching packages. The three role keys below are reserved: every provider setup
/// registers its cache under the matching role key (<c>UseInMemory</c> → <see cref="MemoryCacheProvider"/>,
/// <c>UseRedis</c> → <see cref="RemoteCacheProvider"/>, <c>UseHybrid</c> → <see cref="HybridCacheProvider"/>).
/// The keys are namespaced under <c>Headless.Caching:</c> so they cannot collide with consumer-owned keyed
/// services on the global keyed-service namespace. Named cache instances must not use a reserved name —
/// <c>setup.AddNamed(…)</c> rejects them with <see cref="ArgumentException"/>.
/// </summary>
public static class CacheConstants
{
    public const string RemoteCacheProvider = "Headless.Caching:Remote";
    public const string MemoryCacheProvider = "Headless.Caching:Memory";
    public const string HybridCacheProvider = "Headless.Caching:Hybrid";

    /// <summary>
    /// Indicates whether <paramref name="name"/> is reserved for the caching role registrations: the three
    /// role keys (and any other name under the <c>Headless.Caching:</c> namespace, which the framework owns)
    /// plus the bare short aliases <c>memory</c>/<c>remote</c>/<c>hybrid</c>, rejected to avoid confusion
    /// with the role keys.
    /// </summary>
    /// <param name="name">The candidate cache instance name.</param>
    /// <returns><see langword="true"/> when the name is reserved.</returns>
    public static bool IsReservedProviderKey(string name)
    {
        return name.StartsWith("Headless.Caching:", StringComparison.Ordinal);
    }
}
