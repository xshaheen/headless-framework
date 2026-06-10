// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

public class CacheOptions
{
    /// <summary>Cache key prefix.</summary>
    public string KeyPrefix { get; set; } = "";

    /// <summary>
    /// Default <see cref="CacheEntryOptions"/> for entries created through the option-less
    /// <c>GetOrAddAsync</c> extension overloads. Exposed by the cache instance as
    /// <see cref="ICache.DefaultEntryOptions"/>. When <see langword="null"/> (the default) those overloads
    /// throw <see cref="InvalidOperationException"/> — set this explicitly at registration to opt in.
    /// </summary>
    public CacheEntryOptions? DefaultEntryOptions { get; set; }
}
