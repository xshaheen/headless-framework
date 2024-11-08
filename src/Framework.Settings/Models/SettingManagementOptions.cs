// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;
using Humanizer;

namespace Framework.Settings.Models;

public sealed class SettingManagementOptions
{
    /// <summary>Default: false.</summary>
    public bool IsDynamicSettingStoreEnabled { get; set; }

    /// <summary>Default: true.</summary>
    public bool SaveStaticSettingsToDatabase { get; set; } = true;

    /// <summary>A lock key for the setting update across all the applications.</summary>
    public string CrossApplicationsCommonLockKey { get; set; } = "Common_SettingsUpdateLock";

    /// <summary>Default: 10 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Default: 5 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>Default: 10 minutes.</summary>
    public TimeSpan ApplicationSaveLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Default: 5 minutes.</summary>
    public TimeSpan ApplicationSaveLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>Default: 30 days.</summary>
    public TimeSpan SettingsHashCacheExpiration { get; set; } = 30.Days();

    /// <summary>Default: 30 days.</summary>
    public TimeSpan CommonSettingsUpdatedStampCacheExpiration { get; set; } = 30.Days();

    /// <summary>A stamp when changed the application updates its local cache.</summary>
    public string CommonSettingsUpdatedStampCacheKey { get; set; } = "SettingsUpdatedLocalStamp";

    /// <summary>Default: 30 seconds.</summary>
    public TimeSpan DynamicSettingDefinitionsMemoryCacheExpiration { get; set; } = 30.Seconds();
}

public sealed class SettingManagementOptionsValidator : AbstractValidator<SettingManagementOptions>
{
    public SettingManagementOptionsValidator()
    {
        RuleFor(x => x.CrossApplicationsCommonLockKey).NotEmpty();
        RuleFor(x => x.CrossApplicationsCommonLockExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CrossApplicationsCommonLockAcquireTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ApplicationSaveLockExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ApplicationSaveLockAcquireTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.SettingsHashCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CommonSettingsUpdatedStampCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CommonSettingsUpdatedStampCacheKey).NotEmpty();
    }
}
