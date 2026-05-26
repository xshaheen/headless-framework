// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Storage;

namespace Headless.AuditLog;

[PublicAPI]
public sealed class AuditLogStorageOptions
{
    public string Schema { get; set; } = "audit";

    public string TableName { get; set; } = "audit_log";

    public AuditLogJsonColumnType? JsonColumnType { get; set; }

    public string? CreatedAtColumnType { get; set; }

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
    public static string ToSqlFragment(this AuditLogJsonColumnType columnType) => columnType switch
    {
        AuditLogJsonColumnType.Jsonb => "jsonb",
        AuditLogJsonColumnType.Json => "json",
        AuditLogJsonColumnType.NvarcharMax => "nvarchar(max)",
        _ => throw new ArgumentOutOfRangeException(nameof(columnType), columnType, "Unknown AuditLogJsonColumnType."),
    };
}

internal sealed class AuditLogStorageOptionsValidator : AbstractValidator<AuditLogStorageOptions>
{
    public AuditLogStorageOptionsValidator()
    {
        // Cap at SqlServer's regular-identifier max (128). Shorter PG limits (63 for schema/table)
        // are enforced by the PG initializer's DDL at startup rather than by this shared validator,
        // so SqlServer-only consumers can use schema/table names PG wouldn't accept.
        RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        RuleFor(x => x.TableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        RuleFor(x => x.JsonColumnType).IsInEnum().When(x => x.JsonColumnType.HasValue);
        RuleFor(x => x.CreatedAtColumnType!)
            .MaximumLength(64)
            .Matches(@"^[A-Za-z][A-Za-z0-9 ]*(\([0-9]+\))?$")
            .When(x => !string.IsNullOrEmpty(x.CreatedAtColumnType));
    }
}
