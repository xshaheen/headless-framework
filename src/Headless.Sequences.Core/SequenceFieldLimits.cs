// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>
/// Defines maximum character lengths for sequence key components across database providers.
/// </summary>
/// <remarks>
/// The combined length of tenant identifier, counter name, and partition is 320 characters (640 bytes in UTF-16).
/// This constraint keeps the composite primary key within the 900-byte SQL Server clustered index limit.
/// </remarks>
[PublicAPI]
public static class SequenceFieldLimits
{
    /// <summary>The maximum length of a tenant identifier.</summary>
    public const int TenantIdMaxLength = 128;

    /// <summary>The maximum length of a counter name.</summary>
    public const int NameMaxLength = 128;

    /// <summary>The maximum length of a partition key.</summary>
    public const int PartitionMaxLength = 64;
}
