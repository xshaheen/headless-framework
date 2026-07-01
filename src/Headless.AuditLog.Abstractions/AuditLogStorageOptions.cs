// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Shared storage options for the audit log table, applied by all providers
/// (EntityFramework, PostgreSql, and SqlServer).
/// </summary>
/// <remarks>
/// Configure these options via <see cref="HeadlessAuditLogSetupBuilder.ConfigureStorage"/> inside
/// the <c>AddHeadlessAuditLog(setup =&gt; …)</c> lambda. The EF Core provider validates the schema
/// and table name at startup; raw-SQL providers (PostgreSql, SqlServer) self-initialize the DDL
/// unless <see cref="InitializeOnStartup"/> is set to <see langword="false"/>.
/// </remarks>
[PublicAPI]
public sealed class AuditLogStorageOptions
{
    /// <summary>
    /// Database schema that contains the audit log table. Default: <c>"audit"</c>.
    /// </summary>
    public string Schema { get; set; } = "audit";

    /// <summary>
    /// Name of the audit log table within <see cref="Schema"/>. Default: <c>"audit_log"</c>.
    /// </summary>
    public string TableName { get; set; } = "audit_log";

    /// <summary>
    /// Override the column type used for JSON columns (<c>OldValues</c>, <c>NewValues</c>,
    /// <c>ChangedFields</c>). When <see langword="null"/>, each provider falls back to its own default:
    /// <see cref="AuditLogJsonColumnType.Jsonb"/> for PostgreSql and
    /// <see cref="AuditLogJsonColumnType.NvarcharMax"/> for SqlServer.
    /// </summary>
    /// <remarks>
    /// The PostgreSql provider rejects <see cref="AuditLogJsonColumnType.NvarcharMax"/>;
    /// the SqlServer provider rejects <see cref="AuditLogJsonColumnType.Jsonb"/> and
    /// <see cref="AuditLogJsonColumnType.Json"/>. Both are validated on startup.
    /// </remarks>
    public AuditLogJsonColumnType? JsonColumnType { get; set; }

    /// <summary>
    /// Override the SQL column type for the <c>CreatedAt</c> column (e.g.,
    /// <c>"timestamp with time zone"</c> for PostgreSql, <c>"datetime2"</c> for SqlServer).
    /// When <see langword="null"/> or empty, the provider uses its own default mapping.
    /// </summary>
    public string? CreatedAtColumnType { get; set; }

    /// <summary>
    /// When <see langword="false"/>, the startup storage initializer is skipped — use when the schema is
    /// provisioned out-of-band (migrations job / DBA). The initializer still reports
    /// <c>IsInitialized = true</c> so dependents that await <c>WaitForInitializationAsync</c>
    /// do not block. Only affects raw-DDL self-initializing providers; EF-mode storage uses
    /// EF Core migrations and is not affected by this flag. Default: <see langword="true"/>.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so
    /// adding a new property to this type only requires extending this single method — the
    /// setup pipeline picks it up automatically instead of silently dropping it.
    /// </summary>
    internal void CopyTo(AuditLogStorageOptions target)
    {
        target.Schema = Schema;
        target.TableName = TableName;
        target.JsonColumnType = JsonColumnType;
        target.CreatedAtColumnType = CreatedAtColumnType;
        target.InitializeOnStartup = InitializeOnStartup;
    }
}

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
    public static string ToSqlFragment(this AuditLogJsonColumnType columnType) =>
        columnType switch
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
