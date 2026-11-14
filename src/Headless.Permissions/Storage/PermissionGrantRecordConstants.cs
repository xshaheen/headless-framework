// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Headless.Permissions;

/// <summary>
/// Default column lengths for <see cref="PermissionGrantRecord"/>. <see cref="PermissionsStorageOptions"/> starts from them
/// and is what the schema, the EF Core model, and the repositories read, so configure a length there.
/// </summary>
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
