// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions;

/// <summary>
/// Shared storage configuration for the Headless Permissions system. Applied to whichever storage
/// provider is registered (EF Core, PostgreSQL raw-DDL, or SQL Server raw-DDL).
/// </summary>
[PublicAPI]
public sealed class PermissionsStorageOptions
{
    /// <summary>Database schema that contains all permissions tables. Defaults to <c>"permissions"</c>.</summary>
    public string Schema { get; set; } = "permissions";

    /// <summary>Table name for permission grant records. Defaults to <c>"PermissionGrants"</c>.</summary>
    public string PermissionGrantsTableName { get; set; } = "PermissionGrants";

    /// <summary>Table name for static permission definition records. Defaults to <c>"PermissionDefinitions"</c>.</summary>
    public string PermissionDefinitionsTableName { get; set; } = "PermissionDefinitions";

    /// <summary>Table name for permission group definition records. Defaults to <c>"PermissionGroupDefinitions"</c>.</summary>
    public string PermissionGroupDefinitionsTableName { get; set; } = "PermissionGroupDefinitions";

    /// <summary>
    /// When false, the startup storage initializer is skipped (no-op) — use when the schema is
    /// provisioned out-of-band (migrations job / DBA). The initializer still reports
    /// IsInitialized=true so dependents that await WaitForInitializationAsync do not block. Only
    /// affects raw-DDL self-initializing providers; EF-mode storage uses migrations.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so
    /// adding a new property to this type only requires extending this single method — the
    /// setup pipeline picks it up automatically instead of silently dropping it.
    /// </summary>
    internal void CopyTo(PermissionsStorageOptions target)
    {
        target.Schema = Schema;
        target.PermissionGrantsTableName = PermissionGrantsTableName;
        target.PermissionDefinitionsTableName = PermissionDefinitionsTableName;
        target.PermissionGroupDefinitionsTableName = PermissionGroupDefinitionsTableName;
        target.InitializeOnStartup = InitializeOnStartup;
    }
}
