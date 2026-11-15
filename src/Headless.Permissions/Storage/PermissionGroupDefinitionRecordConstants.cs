// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions;

/// <summary>
/// Default column lengths for <see cref="PermissionGroupDefinitionRecord"/>. <see cref="PermissionsStorageOptions"/> starts from them
/// and is what the schema, the EF Core model, and the repositories read, so configure a length there.
/// </summary>
public static class PermissionGroupDefinitionRecordConstants
{
    /// <summary>Maximum length of <see cref="PermissionGroupDefinitionRecord.Name"/> (128 characters).</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length of <see cref="PermissionGroupDefinitionRecord.DisplayName"/> (256 characters).</summary>
    public const int DisplayNameMaxLength = 256;
}
