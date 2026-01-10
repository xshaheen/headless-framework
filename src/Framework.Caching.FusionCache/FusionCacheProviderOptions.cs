// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Caching;

/// <summary>
/// Options for the FusionCache provider implementing <see cref="IHybridCache"/>.
/// </summary>
[PublicAPI]
public sealed class FusionCacheProviderOptions : HybridCacheOptions
{
    /// <summary>
    /// A unique name for this cache instance. Used for identification in logs and metrics.
    /// </summary>
    public string CacheName { get; set; } = "default";

    /// <summary>
    /// The default duration for L2 (distributed) cache soft timeout.
    /// After this timeout, a stale value may be returned while background refresh continues.
    /// </summary>
    public TimeSpan DistributedCacheSoftTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The default duration for L2 (distributed) cache hard timeout.
    /// After this timeout, the operation will fail if no value is available.
    /// </summary>
    public TimeSpan DistributedCacheHardTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When <see langword="true"/>, allows L2 cache operations to complete in the background
    /// rather than blocking the caller.
    /// </summary>
    public bool AllowBackgroundDistributedCacheOperations { get; set; } = true;

    /// <summary>
    /// Maximum jitter to add to cache durations to prevent thundering herd on expiration.
    /// A random value between 0 and this duration will be added to each entry's TTL.
    /// </summary>
    public TimeSpan JitterMaxDuration { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Validator for <see cref="FusionCacheProviderOptions"/>.
/// </summary>
public sealed class FusionCacheProviderOptionsValidator : AbstractValidator<FusionCacheProviderOptions>
{
    public FusionCacheProviderOptionsValidator()
    {
        RuleFor(x => x.CacheName).NotEmpty();
        RuleFor(x => x.DefaultDuration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.FailSafeMaxDuration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.FactoryTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.DistributedCacheSoftTimeout).GreaterThanOrEqualTo(TimeSpan.Zero);
        RuleFor(x => x.DistributedCacheHardTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.JitterMaxDuration).GreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
