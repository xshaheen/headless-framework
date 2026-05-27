// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions;

[PublicAPI]
public sealed class PermissionsStorageOptions
{
    public string Schema { get; set; } = "permissions";

    public string PermissionGrantsTableName { get; set; } = "PermissionGrants";

    public string PermissionDefinitionsTableName { get; set; } = "PermissionDefinitions";

    public string PermissionGroupDefinitionsTableName { get; set; } = "PermissionGroupDefinitions";
}
