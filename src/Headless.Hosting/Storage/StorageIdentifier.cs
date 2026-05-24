// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Storage;

/// <summary>
/// Identifier-pattern constants used by storage-provider options validators to reject SQL identifier
/// injection in <c>Schema</c> / <c>*TableName</c> properties that are later interpolated into raw
/// DDL/DML scripts. PostgreSQL allows up to 63 characters; SQL Server allows up to 128.
/// </summary>
[PublicAPI]
public static class StorageIdentifier
{
    /// <summary>Pattern enforced on storage identifiers (schema/table names). Letters, digits, underscores; first char non-digit.</summary>
    public const string PgPattern = "^[A-Za-z_][A-Za-z0-9_]*$";

    /// <summary>PostgreSQL identifier length cap (NAMEDATALEN - 1 = 63).</summary>
    public const int PgMaxLength = 63;

    /// <summary>SQL Server regular identifier length cap.</summary>
    public const int SqlServerMaxLength = 128;
}
