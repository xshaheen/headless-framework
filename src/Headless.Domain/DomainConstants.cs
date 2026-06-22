// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Shared column-length constants used when mapping domain entities to a relational schema.</summary>
[PublicAPI]
public static class DomainConstants
{
    /// <summary>Maximum column length for entity identifier fields (covers UUID v7 string representation).</summary>
    public const int IdMaxLength = 41;

    /// <summary>Maximum column length for concurrency-stamp fields.</summary>
    public const int ConcurrencyStampMaxLength = 40;

    /// <summary>Maximum column length for enum name fields stored as strings.</summary>
    public const int EnumMaxLength = 100;
}
