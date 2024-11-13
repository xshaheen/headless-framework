// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Humanizer;

namespace Framework.Permissions.Models;

public sealed class PermissionManagementOptions
{
    /// <summary>Default: true.</summary>
    public bool SaveStaticPermissionsToDatabase { get; set; } = true;

    /// <summary>Default: false.</summary>
    public bool IsDynamicPermissionStoreEnabled { get; set; }

    /// <summary>A lock key for the permission update across all the applications.</summary>
    public string CrossApplicationsCommonLockKey { get; set; } = "Common_PermissionsUpdateLock";

    /// <summary>Default: 10 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Default: 5 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>Default: 10 minutes.</summary>
    public TimeSpan ApplicationSaveLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Default: 5 minutes.</summary>
    public TimeSpan ApplicationSaveLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>Default: 30 days.</summary>
    public TimeSpan PermissionsHashCacheExpiration { get; set; } = 30.Days();

    /// <summary>Default: 30 days.</summary>
    public TimeSpan CommonPermissionsUpdatedStampCacheExpiration { get; set; } = 30.Days();

    /// <summary>A stamp when changed the application updates its local cache.</summary>
    public string CommonPermissionsUpdatedStampCacheKey { get; set; } = "PermissionsUpdatedLocalStamp";

    /// <summary>Default: 30 seconds.</summary>
    public TimeSpan DynamicDefinitionsMemoryCacheExpiration { get; set; } = 30.Seconds();
}

public sealed class PermissionManagementOptionsValidator : AbstractValidator<PermissionManagementOptions>
{
    public PermissionManagementOptionsValidator()
    {
        RuleFor(x => x.CrossApplicationsCommonLockKey).NotEmpty();
        RuleFor(x => x.CrossApplicationsCommonLockExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CrossApplicationsCommonLockAcquireTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ApplicationSaveLockExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ApplicationSaveLockAcquireTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.PermissionsHashCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CommonPermissionsUpdatedStampCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CommonPermissionsUpdatedStampCacheKey).NotEmpty();
    }
}
