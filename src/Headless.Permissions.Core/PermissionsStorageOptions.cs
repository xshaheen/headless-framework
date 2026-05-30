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
    }
}
