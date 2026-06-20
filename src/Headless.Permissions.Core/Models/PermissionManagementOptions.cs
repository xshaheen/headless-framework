// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Humanizer;

namespace Headless.Permissions.Models;

/// <summary>
/// Options that control the permission management subsystem: whether static definitions are synced to the
/// database, whether the dynamic store is active, distributed-lock keys/timeouts, and cache expiry durations.
/// Validated at startup via <c>PermissionManagementOptionsValidator</c>.
/// </summary>
public sealed class PermissionManagementOptions
{
    /// <summary>
    /// When <see langword="true"/> (the default), the application persists its static permission definitions to
    /// the database on startup via <see cref="Definitions.IDynamicPermissionDefinitionStore.SaveAsync"/> so other
    /// instances can read them through the dynamic store.
    /// </summary>
    public bool SaveStaticPermissionsToDatabase { get; set; } = true;

    /// <summary>
    /// When <see langword="false"/> (the default), all <see cref="Definitions.IDynamicPermissionDefinitionStore"/>
    /// read operations return empty/null without hitting the database. Set to <see langword="true"/> to enable
    /// DB-backed dynamic permissions.
    /// </summary>
    public bool IsDynamicPermissionStoreEnabled { get; set; }

    /// <summary>
    /// Distributed-lock resource key used to serialize cross-application permission stamp and save operations.
    /// Must be unique per deployment environment. Default: <c>permissions:common_update_lock</c>.
    /// </summary>
    public string CrossApplicationsCommonLockKey { get; set; } = "permissions:common_update_lock";

    /// <summary>How long the <see cref="CrossApplicationsCommonLockKey"/> distributed lock is held. Default: 10 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Maximum time to wait when acquiring <see cref="CrossApplicationsCommonLockKey"/>. Default: 5 minutes.</summary>
    public TimeSpan CrossApplicationsCommonLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>How long the per-application save lock is held, preventing concurrent saves from the same app. Default: 10 minutes.</summary>
    public TimeSpan ApplicationSaveLockExpiration { get; set; } = 10.Minutes();

    /// <summary>Maximum time to wait when acquiring the per-application save lock. Default: 5 minutes.</summary>
    public TimeSpan ApplicationSaveLockAcquireTimeout { get; set; } = 5.Minutes();

    /// <summary>
    /// How long the MD5 hash of the last successfully saved permission set is cached in the distributed cache.
    /// The hash is used to skip no-op saves. Default: 30 days.
    /// </summary>
    public TimeSpan PermissionsHashCacheExpiration { get; set; } = 30.Days();

    /// <summary>
    /// How long the cross-application update stamp is kept in the distributed cache before it must be regenerated.
    /// Default: 30 days.
    /// </summary>
    public TimeSpan CommonPermissionsUpdatedStampCacheExpiration { get; set; } = 30.Days();

    /// <summary>
    /// Distributed-cache key for the cross-application update stamp. When this stamp changes, each application
    /// instance refreshes its in-memory definition cache on the next read. Default: <c>permissions:updated_local_stamp</c>.
    /// </summary>
    public string CommonPermissionsUpdatedStampCacheKey { get; set; } = "permissions:updated_local_stamp";

    /// <summary>
    /// How long an application instance may serve definitions from its in-memory cache before re-checking the
    /// distributed stamp. Lower values reduce staleness at the cost of more distributed-cache reads. Default: 30 seconds.
    /// </summary>
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
