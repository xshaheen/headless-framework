// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// Maximum lengths of the three lease-key parts. Call validation and every provider's table definition read these,
/// so a key that passes validation always fits its column.
/// </summary>
/// <remarks>
/// Together they total 448 characters, 896 bytes as <c>nvarchar</c>, which keeps the SQL Server clustered key under
/// its 900-byte limit. Past that limit SQL Server creates the table with only a warning and then fails the insert at
/// runtime, after validation has already accepted the key.
/// </remarks>
[PublicAPI]
public static class FencingFieldLimits
{
    /// <summary>The maximum length of a tenant id.</summary>
    public const int TenantIdMaxLength = 128;

    /// <summary>The maximum length of a lease kind.</summary>
    public const int KindMaxLength = 64;

    /// <summary>The maximum length of a leased resource.</summary>
    public const int ResourceMaxLength = 256;
}
