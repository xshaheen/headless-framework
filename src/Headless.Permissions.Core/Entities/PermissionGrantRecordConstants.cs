// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Headless.Permissions.Entities;

/// <summary>Column-length constraints for <see cref="PermissionGrantRecord"/> enforced at both the entity
/// constructor and the database schema level.</summary>
public static class PermissionGrantRecordConstants
{
    /// <summary>Maximum length of <see cref="PermissionGrantRecord.Name"/> (128 characters).</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length of <see cref="PermissionGrantRecord.ProviderName"/> (64 characters).</summary>
    public const int ProviderNameMaxLength = 64;

    /// <summary>Maximum length of <see cref="PermissionGrantRecord.ProviderKey"/> (64 characters).</summary>
    public const int ProviderKeyMaxLength = 64;

    /// <summary>
    /// Maximum length of <see cref="PermissionGrantRecord.TenantId"/>; mirrors
    /// <see cref="DomainConstants.IdMaxLength"/>.
    /// </summary>
    public const int TenantIdMaxLength = DomainConstants.IdMaxLength;
}
