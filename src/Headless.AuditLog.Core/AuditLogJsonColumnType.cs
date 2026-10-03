// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.AuditLog;

/// <summary>SQL column type used for JSON columns in the audit log table.</summary>
[PublicAPI]
public enum AuditLogJsonColumnType
{
    /// <summary>
    /// PostgreSQL <c>jsonb</c> binary JSON column. Supports indexing and operator queries.
    /// The default for the PostgreSql provider.
    /// </summary>
    Jsonb = 0,

    /// <summary>
    /// PostgreSQL <c>json</c> text JSON column. Stored as-is; no binary parsing on write.
    /// Supported by the PostgreSql provider only.
    /// </summary>
    Json = 1,

    /// <summary>
    /// SQL Server <c>nvarchar(max)</c> column storing JSON as Unicode text.
    /// The default for the SqlServer provider and the EF Core provider when targeting SqlServer.
    /// </summary>
    NvarcharMax = 2,
}

internal static class AuditLogJsonColumnTypeExtensions
{
    public static string ToSqlFragment(this AuditLogJsonColumnType columnType)
    {
        return columnType switch
        {
            AuditLogJsonColumnType.Jsonb => "jsonb",
            AuditLogJsonColumnType.Json => "json",
            AuditLogJsonColumnType.NvarcharMax => "nvarchar(max)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(columnType),
                columnType,
                "Unknown AuditLogJsonColumnType."
            ),
        };
    }
}
