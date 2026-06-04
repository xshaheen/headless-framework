// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

[PublicAPI]
public sealed class AuditLogStorageOptions
{
    public string Schema { get; set; } = "audit";

    public string TableName { get; set; } = "audit_log";

    public AuditLogJsonColumnType? JsonColumnType { get; set; }

    public string? CreatedAtColumnType { get; set; }

    /// <summary>
    /// When false, the startup storage initializer is skipped (no-op) — use when the schema is
    /// provisioned out-of-band (migrations job / DBA). The initializer still reports
    /// IsInitialized=true so dependents that await WaitForInitializationAsync do not block. Only
    /// affects raw-DDL self-initializing providers; EF-mode storage uses migrations.
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

[PublicAPI]
public enum AuditLogJsonColumnType
{
    Jsonb = 0,
    Json = 1,
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
