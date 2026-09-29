// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Permissions;

/// <summary>
/// Shared storage configuration for the Headless Permissions system. Applied to whichever storage
/// provider is registered (EF Core, PostgreSQL raw-DDL, or SQL Server raw-DDL).
/// </summary>
[PublicAPI]
public sealed class PermissionsStorageOptions
{
    /// <summary>
    /// Database schema that contains all permissions tables. Defaults to
    /// <see cref="HeadlessStorageDefaults.Schema"/> (<c>"headless"</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>
    /// Table name for permission grant records. Default: <see langword="null"/>, which uses the database's
    /// conventional name: <c>permission_grants</c> on PostgreSQL, <c>PermissionGrants</c> elsewhere. A configured
    /// name is used verbatim, and the table's key and index names derive from it.
    /// </summary>
    public string? PermissionGrantsTableName { get; set; }

    /// <summary>
    /// Table name for static permission definition records. Default: <see langword="null"/>, which uses the
    /// database's conventional name: <c>permission_definitions</c> on PostgreSQL, <c>PermissionDefinitions</c>
    /// elsewhere. A configured name is used verbatim.
    /// </summary>
    public string? PermissionDefinitionsTableName { get; set; }

    /// <summary>
    /// Table name for permission group definition records. Default: <see langword="null"/>, which uses the
    /// database's conventional name: <c>permission_group_definitions</c> on PostgreSQL,
    /// <c>PermissionGroupDefinitions</c> elsewhere. A configured name is used verbatim.
    /// </summary>
    public string? PermissionGroupDefinitionsTableName { get; set; }

    /// <summary>
    /// When false, the startup storage initializer is skipped (no-op) — use when the schema is
    /// provisioned out-of-band (migrations job / DBA). The initializer still reports
    /// IsInitialized=true so dependents that await WaitForInitializationAsync do not block. Only
    /// affects raw-DDL self-initializing providers; EF-mode storage uses migrations.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    /// <summary>Returns the permission grants table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolvePermissionGrantsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(PermissionGrantsTableName, style, "PermissionGrants");
    }

    /// <summary>Returns the permission definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolvePermissionDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(PermissionDefinitionsTableName, style, "PermissionDefinitions");
    }

    /// <summary>Returns the permission group definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolvePermissionGroupDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(PermissionGroupDefinitionsTableName, style, "PermissionGroupDefinitions");
    }

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
