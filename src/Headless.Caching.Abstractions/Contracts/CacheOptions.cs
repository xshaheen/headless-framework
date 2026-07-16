// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Base options shared by every cache provider (for example <c>InMemoryCacheOptions</c>, <c>RedisCacheOptions</c>,
/// <c>HybridCacheOptions</c>). Provider-specific options extend this class.
/// </summary>
[PublicAPI]
public class CacheOptions
{
    /// <summary>
    /// Gets or sets the string prepended to every cache key before it is sent to the backing store,
    /// providing a simple namespace to isolate this instance's keys from other consumers of the same store.
    /// Defaults to an empty string (no prefix).
    /// </summary>
    public string KeyPrefix { get; set; } = "";

    /// <summary>
    /// The registered cache-instance name surfaced on the <c>headless.cache.name</c> telemetry dimension. Set at
    /// registration for named instances; <see langword="null"/> for the unkeyed default (which reports as
    /// <c>"default"</c>). Instrumentation metadata only — it does not affect cache behavior.
    /// </summary>
    public string? CacheName { get; set; }

    /// <summary>
    /// Default <see cref="CacheEntryOptions"/> for entries created through the option-less
    /// <c>GetOrAddAsync</c> extension overloads. Exposed by the cache instance as
    /// <see cref="ICache.DefaultEntryOptions"/>. When <see langword="null"/> (the default) those overloads
    /// throw <see cref="InvalidOperationException"/> — set this explicitly at registration to opt in.
    /// </summary>
    public CacheEntryOptions? DefaultEntryOptions { get; set; }
}
