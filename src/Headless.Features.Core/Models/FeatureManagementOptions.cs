// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Humanizer;

namespace Headless.Features.Models;

/// <summary>Configuration options for the feature management system.</summary>
public sealed class FeatureManagementOptions
{
    /// <summary>
    /// When <see langword="true"/>, static feature definitions are persisted to the database on startup so they are
    /// available to all application instances via the dynamic store. Default: <see langword="true"/>.
    /// </summary>
    public bool SaveStaticFeaturesToDatabase { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, the <see cref="Headless.Features.Definitions.IDynamicFeatureDefinitionStore"/> is consulted for feature definitions
    /// in addition to the static store. Default: <see langword="false"/>.
    /// </summary>
    public bool IsDynamicFeatureStoreEnabled { get; set; }

    /// <summary>Distributed lock resource key used to coordinate feature definition updates across all application instances. Default: <c>features:common_update_lock</c>.</summary>
    public string CrossApplicationsCommonLockKey { get; set; } = "features:common_update_lock";

    /// <summary>How long the cross-application common lock may be held before it expires. Default: 10 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Maximum time to wait when acquiring the cross-application common lock. Default: 5 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>How long the per-application save lock may be held before it expires. Default: 10 minutes.</summary>
    public TimeSpan ApplicationSaveLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Maximum time to wait when acquiring the per-application save lock. Default: 5 minutes.</summary>
    public TimeSpan ApplicationSaveLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>How long the per-application feature hash is cached in the distributed cache. Default: 30 days.</summary>
    public TimeSpan FeaturesHashCacheExpiration { get; set; } = 30.Days();

    /// <summary>How long the shared "features updated" stamp is retained in the distributed cache. Default: 30 days.</summary>
    public TimeSpan CommonFeaturesUpdatedStampCacheExpiration { get; set; } = 30.Days();

    /// <summary>
    /// Distributed cache key for the shared stamp that signals all instances to refresh their in-process caches.
    /// Default: <c>features:updated_local_stamp</c>.
    /// </summary>
    public string CommonFeaturesUpdatedStampCacheKey { get; set; } = "features:updated_local_stamp";

    /// <summary>
    /// How long dynamic feature definitions are held in the in-process memory cache before the distributed cache stamp
    /// is re-checked. Default: 30 seconds.
    /// </summary>
    public TimeSpan DynamicDefinitionsMemoryCacheExpiration { get; set; } = 30.Seconds();

    /// <summary>
    /// Optional named cache instance or store role-key used for feature-value caching.
    /// When <see langword="null"/> or empty, the default registered <c>ICache</c> is used.
    /// </summary>
    public string? FeatureValueCacheName { get; set; }
}

/// <summary>Validates <see cref="FeatureManagementOptions"/> on startup.</summary>
public sealed class FeatureManagementOptionsValidator : AbstractValidator<FeatureManagementOptions>
{
    public FeatureManagementOptionsValidator()
    {
        RuleFor(x => x.CrossApplicationsCommonLockKey).NotEmpty();
        RuleFor(x => x.CrossApplicationsCommonLockExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CrossApplicationsCommonLockAcquireTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ApplicationSaveLockExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ApplicationSaveLockAcquireTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.FeaturesHashCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CommonFeaturesUpdatedStampCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CommonFeaturesUpdatedStampCacheKey).NotEmpty();
    }
}
