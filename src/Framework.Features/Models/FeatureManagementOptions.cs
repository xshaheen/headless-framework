// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;
using Humanizer;

namespace Framework.Features.Models;

public sealed class FeatureManagementOptions
{
    /// <summary>Default: true.</summary>
    public bool SaveStaticFeaturesToDatabase { get; set; } = true;

    /// <summary>Default: false.</summary>
    public bool IsDynamicFeatureStoreEnabled { get; set; }

    /// <summary>A lock key for the feature update across all the applications.</summary>
    public string CrossApplicationsCommonLockKey { get; set; } = "Common_FeaturesUpdateLock";

    /// <summary>Default: 10 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Default: 5 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>Default: 10 minutes.</summary>
    public TimeSpan ApplicationSaveLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Default: 5 minutes.</summary>
    public TimeSpan ApplicationSaveLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>Default: 30 days.</summary>
    public TimeSpan FeaturesHashCacheExpiration { get; set; } = 30.Days();

    /// <summary>Default: 30 days.</summary>
    public TimeSpan CommonFeaturesUpdatedStampCacheExpiration { get; set; } = 30.Days();

    /// <summary>A stamp when changed the application updates its local cache.</summary>
    public string CommonFeaturesUpdatedStampCacheKey { get; set; } = "FeaturesUpdatedLocalStamp";

    /// <summary>Default: 30 seconds.</summary>
    public TimeSpan DynamicDefinitionsMemoryCacheExpiration { get; set; } = 30.Seconds();
}

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
