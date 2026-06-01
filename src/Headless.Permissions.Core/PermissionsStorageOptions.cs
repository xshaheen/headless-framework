// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions;

[PublicAPI]
public sealed class PermissionsStorageOptions
{
    public string Schema { get; set; } = "permissions";

    public string PermissionGrantsTableName { get; set; } = "PermissionGrants";

    public string PermissionDefinitionsTableName { get; set; } = "PermissionDefinitions";

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
