// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.AuditLog.PostgreSql;

/// <summary>Resolves the audit log table's PostgreSQL names shared by the schema contribution, writer, and reader.</summary>
internal static class PostgreSqlAuditLogSchema
{
    // Quoted so a configured table name keeps its exact case; the default snake_case name reads the same unquoted.
    public static string Qualified(AuditLogStorageOptions options)
    {
        return $"""
            "{options.Schema}"."{TableName(options)}"
            """;
    }

    public static string TableName(AuditLogStorageOptions options)
    {
        return options.ResolveTableName(StorageNamingStyle.SnakeCase);
    }
}
