// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Entities;

/// <summary>Column-length constraints for <see cref="PermissionDefinitionRecord"/> enforced at both the entity
/// constructor and the database schema level.</summary>
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
