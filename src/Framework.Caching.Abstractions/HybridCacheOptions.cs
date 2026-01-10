// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Caching;

/// <summary>
/// Base options for <see cref="IHybridCache"/> implementations.
/// Provider-specific options should extend this class.
/// </summary>
[PublicAPI]
public class HybridCacheOptions
{
    /// <summary>
    /// A prefix to prepend to all cache keys.
    /// Useful for isolating cache entries in shared cache instances.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// The default duration for cache entries when not specified per-operation.
    /// </summary>
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When <see langword="true"/>, enables fail-safe behavior by default.
    /// Individual operations can override this via <see cref="HybridCacheEntryOptions.EnableFailSafe"/>.
    /// </summary>
    public bool EnableFailSafe { get; set; } = true;

    /// <summary>
    /// The default maximum duration to keep stale values for fail-safe fallback.
    /// Only applies when <see cref="EnableFailSafe"/> is <see langword="true"/>.
    /// </summary>
    public TimeSpan FailSafeMaxDuration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// The default timeout for factory execution.
    /// Operations can override this via <see cref="HybridCacheEntryOptions.FactoryTimeout"/>.
    /// </summary>
    public TimeSpan FactoryTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
