// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Entities;

/// <summary>Column-length constraints for <see cref="PermissionGroupDefinitionRecord"/> enforced at both the entity
/// constructor and the database schema level.</summary>
public static class PermissionGroupDefinitionRecordConstants
{
    /// <summary>Maximum length of <see cref="PermissionGroupDefinitionRecord.Name"/> (128 characters).</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length of <see cref="PermissionGroupDefinitionRecord.DisplayName"/> (256 characters).</summary>
    public const int DisplayNameMaxLength = 256;
}
