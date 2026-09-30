// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.AuditLog.SqlServer;

/// <summary>Resolves the audit log table's SQL Server names shared by the schema contribution, writer, and reader.</summary>
internal static class SqlServerAuditLogSchema
{
    public static string Qualified(AuditLogStorageOptions options)
    {
        return $"[{options.Schema}].[{TableName(options)}]";
    }

    public static string ObjectName(AuditLogStorageOptions options)
    {
        return $"{options.Schema}.{TableName(options)}";
    }

    public static string TableName(AuditLogStorageOptions options)
    {
        return options.ResolveTableName(StorageNamingStyle.PascalCase);
    }
}
