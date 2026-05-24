// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting.Storage;

namespace Headless.AuditLog;

[PublicAPI]
public sealed class AuditLogStorageOptions
{
    public string Schema { get; set; } = "audit";

    public string TableName { get; set; } = "audit_log";

    public AuditLogJsonColumnType? JsonColumnType { get; set; }

    public string? CreatedAtColumnType { get; set; }
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
        RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.TableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.JsonColumnType).IsInEnum().When(x => x.JsonColumnType.HasValue);
    }
}
