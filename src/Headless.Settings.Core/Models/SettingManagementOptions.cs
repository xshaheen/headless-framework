// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Humanizer;

namespace Headless.Settings.Models;

/// <summary>Options controlling the runtime behaviour of the settings management system.</summary>
public sealed class SettingManagementOptions
{
    /// <summary>
    /// Gets or sets whether <see cref="Headless.Settings.Definitions.IDynamicSettingDefinitionStore"/> is active and should serve definitions
    /// from the database. Default: <see langword="false"/>.
    /// </summary>
    public bool IsDynamicSettingStoreEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether static setting definitions are persisted to the database on startup.
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool SaveStaticSettingsToDatabase { get; set; } = true;

    /// <summary>
    /// Gets or sets the distributed-lock key used to coordinate cross-application setting updates.
    /// Default: <c>settings:common_update_lock</c>.
    /// </summary>
    public string CrossApplicationsCommonLockKey { get; set; } = "settings:common_update_lock";

    /// <summary>Gets or sets how long the cross-application common lock is held before it expires. Default: 10 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Gets or sets how long to wait when attempting to acquire the cross-application common lock. Default: 5 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>Gets or sets how long the per-application save lock is held before it expires. Default: 10 minutes.</summary>
    public TimeSpan ApplicationSaveLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Gets or sets how long to wait when attempting to acquire the per-application save lock. Default: 5 minutes.</summary>
    public TimeSpan ApplicationSaveLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>Gets or sets the lifetime of cached setting values in the distributed cache. Default: 5 hours.</summary>
    public TimeSpan ValueCacheExpiration { get; set; } = 5.Hours();

    /// <summary>Gets or sets the lifetime of the settings hash stamp in the distributed cache, used to detect changes. Default: 30 days.</summary>
    public TimeSpan SettingsHashCacheExpiration { get; set; } = 30.Days();

    /// <summary>Gets or sets the lifetime of the common updated-stamp entry in the distributed cache. Default: 30 days.</summary>
    public TimeSpan CommonSettingsUpdatedStampCacheExpiration { get; set; } = 30.Days();

    /// <summary>
    /// Gets or sets the distributed-cache key for the shared updated stamp.
    /// When this stamp changes, each application instance refreshes its local in-memory cache.
    /// Default: <c>settings:updated_local_stamp</c>.
    /// </summary>
    public string CommonSettingsUpdatedStampCacheKey { get; set; } = "settings:updated_local_stamp";

    /// <summary>
    /// Gets or sets how long the local in-memory definition cache is considered fresh before re-checking
    /// the distributed stamp. Default: 30 seconds.
    /// </summary>
    public TimeSpan DynamicDefinitionsMemoryCacheExpiration { get; set; } = 30.Seconds();
}

/// <summary>Validator for <see cref="SettingManagementOptions"/>.</summary>
public sealed class SettingManagementOptionsValidator : AbstractValidator<SettingManagementOptions>
{
    public SettingManagementOptionsValidator()
    {
        RuleFor(x => x.CrossApplicationsCommonLockKey).NotEmpty();
        RuleFor(x => x.CrossApplicationsCommonLockExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CrossApplicationsCommonLockAcquireTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ApplicationSaveLockExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ApplicationSaveLockAcquireTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.ValueCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.SettingsHashCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CommonSettingsUpdatedStampCacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CommonSettingsUpdatedStampCacheKey).NotEmpty();
    }
}
