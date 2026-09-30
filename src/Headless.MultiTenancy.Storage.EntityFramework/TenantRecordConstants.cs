// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>Column length limits for <see cref="TenantRecord"/> fields.</summary>
public static class TenantRecordConstants
{
    /// <summary>
    /// Maximum character length for <see cref="TenantRecord.Identifier"/> and
    /// <see cref="TenantRecord.NormalizedIdentifier"/>. Equals <see cref="TenantCatalogOptions.MaxIdentifierLengthLimit"/>
    /// (253, the DNS hostname limit) so every identifier the catalog can accept, including a whole-host
    /// custom-domain identifier, fits the column.
    /// </summary>
    public const int IdentifierMaxLength = TenantCatalogOptions.MaxIdentifierLengthLimit;

    /// <summary>Maximum character length for <see cref="TenantRecord.Name"/>.</summary>
    public const int NameMaxLength = 256;
}
