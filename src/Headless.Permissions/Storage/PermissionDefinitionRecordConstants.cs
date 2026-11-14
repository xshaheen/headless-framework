// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions;

/// <summary>
/// Default column lengths for <see cref="PermissionDefinitionRecord"/>. <see cref="PermissionsStorageOptions"/> starts from them
/// and is what the schema, the EF Core model, and the repositories read, so configure a length there.
/// </summary>
public static class PermissionDefinitionRecordConstants
{
    /// <summary>Maximum length of <see cref="PermissionDefinitionRecord.Name"/> (128 characters).</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length of <see cref="PermissionDefinitionRecord.DisplayName"/> (256 characters).</summary>
    public const int DisplayNameMaxLength = 256;

    /// <summary>
    /// Maximum length of the comma-joined <see cref="PermissionDefinitionRecord.Providers"/> string (128 characters).
    /// </summary>
    public const int ProvidersMaxLength = 128;
}
