// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>Column length limits for <see cref="TenantRecord"/> fields.</summary>
public static class TenantRecordConstants
{
    /// <summary>
    /// Maximum character length for <see cref="TenantRecord.Identifier"/> and
    /// <see cref="TenantRecord.NormalizedIdentifier"/>. Generous relative to
    /// <c>TenantCatalogOptions.MaxIdentifierLength</c>'s default of 63 (DNS-label form) so a custom,
    /// longer identifier shape still fits.
    /// </summary>
    public const int IdentifierMaxLength = 128;

    /// <summary>Maximum character length for <see cref="TenantRecord.Name"/>.</summary>
    public const int NameMaxLength = 256;
}
