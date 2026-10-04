// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Defines base options shared by cache providers. Provider-specific options extend this class.
/// </summary>
[PublicAPI]
public class CacheOptions
{
    /// <summary>
    /// Gets or sets the string prepended to every cache key before sending it to the backing store.
    /// Provides namespace isolation from other consumers sharing the same store.
    /// Defaults to an empty string.
    /// </summary>
    public string KeyPrefix { get; set; } = "";

    /// <summary>
    /// Gets or sets the registered cache instance name surfaced on the <c>headless.cache.name</c> telemetry dimension.
    /// Set at registration for named instances, or <see langword="null"/> for the unkeyed default instance.
    /// This property contains instrumentation metadata only and does not change cache behavior.
    /// </summary>
    public string? CacheName { get; set; }

    /// <summary>
    /// Gets or sets the default <see cref="CacheEntryOptions"/> for entries created through overloads that omit options.
    /// When <see langword="null"/>, those overloads throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public CacheEntryOptions? DefaultEntryOptions { get; set; }
}
