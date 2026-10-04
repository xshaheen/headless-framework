// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// The PascalCase parts, after the table name, of every index the audit-log table carries. The raw providers and the
/// EF mapping build their index names from them, and the option validators measure the names they derive from a
/// configured table name.
/// </summary>
/// <remarks>
/// PostgreSQL index names are unique per schema, not per table, so every derived name embeds the table name: a fixed
/// name would let the first audit table in a schema claim every index and leave a second table (same schema,
/// different table name) with none.
/// </remarks>
internal static class AuditLogStorageNames
{
    /// <summary>The PascalCase default table name, converted to each database's convention.</summary>
    public const string DefaultTableName = "AuditLogEntries";

    public static readonly string[] TenantTime = ["TenantTime"];

    public static readonly string[] TenantActionTime = ["TenantActionTime"];

    public static readonly string[] TenantEntityTime = ["TenantEntityTime"];

    public static readonly string[] TenantActorTime = ["TenantActorTime"];

    public static readonly string[] TenantAccountTime = ["TenantAccountTime"];

    public static readonly string[] Correlation = ["Correlation"];

    public static readonly string[][] Indexes =
    [
        TenantTime,
        TenantActionTime,
        TenantEntityTime,
        TenantActorTime,
        TenantAccountTime,
        Correlation,
    ];
}
