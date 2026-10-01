// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;

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

    /// <summary>The <c>CreatedAt</c> column's type: the configured override, else <c>datetimeoffset(7)</c>.</summary>
    public static string CreatedAtColumnType(AuditLogStorageOptions options)
    {
        return string.IsNullOrWhiteSpace(options.CreatedAtColumnType)
            ? "datetimeoffset(7)"
            : options.CreatedAtColumnType;
    }

    /// <summary>
    /// Binds a UTC instant typed like the <c>CreatedAt</c> column, so a comparison never converts the column and keeps
    /// its index usable. An untyped <c>DateTime</c> would bind as legacy <c>datetime</c> and round to about 3 ms,
    /// collapsing distinct timestamps that read paging orders by.
    /// </summary>
    public static SqlParameter CreatedAtParameter(AuditLogStorageOptions options, string name, DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();

        return CreatedAtColumnType(options).StartsWith("datetimeoffset", StringComparison.OrdinalIgnoreCase)
            ? new SqlParameter(name, SqlDbType.DateTimeOffset) { Value = utc }
            : new SqlParameter(name, SqlDbType.DateTime2) { Value = utc.UtcDateTime };
    }

    /// <summary>Reads a <c>CreatedAt</c> value as a UTC instant, whichever date type the column has.</summary>
    public static DateTimeOffset ReadCreatedAt(SqlDataReader reader, int ordinal)
    {
        return reader.GetValue(ordinal) switch
        {
            DateTimeOffset instant => instant.ToUniversalTime(),
            DateTime utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            var other => throw new InvalidCastException(
                $"The audit log CreatedAt column returned a {other.GetType().Name}; it must be a date and time type."
            ),
        };
    }
}
