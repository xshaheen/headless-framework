// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.AuditLog;

/// <summary>
/// Names of the audit-log table's primary key and indexes, derived from
/// <see cref="AuditLogStorageOptions.TableName"/> and shared by every storage provider.
/// </summary>
/// <remarks>
/// PostgreSQL index names are unique per schema, not per table, so a fixed name would let the first audit
/// table in a schema claim every index and leave a second table (same schema, different table name) with none.
/// </remarks>
internal static class AuditLogStorageNames
{
    // PostgreSQL silently truncates longer identifiers, which would make two derived names collide. It is the
    // strictest supported provider, so every provider applies this bound and a table name valid on one stays
    // valid on the others.
    private const int _MaxIdentifierBytes = StorageIdentifier.PostgreSql.IdentifierMaxLength;

    /// <summary>The longest <see cref="AuditLogStorageOptions.TableName"/> whose derived names all fit 63 bytes.</summary>
    public static readonly int MaxTableNameLength = _MaxIdentifierBytes - _LongestDerivedNameOverhead();

    public static string PrimaryKey(string tableName) => $"PK_{tableName}";

    public static string TenantTimeIndex(string tableName) => $"ix_{tableName}_tenant_time";

    public static string TenantActionTimeIndex(string tableName) => $"ix_{tableName}_tenant_action_time";

    public static string TenantEntityTimeIndex(string tableName) => $"ix_{tableName}_tenant_entity_time";

    public static string TenantActorTimeIndex(string tableName) => $"ix_{tableName}_tenant_actor_time";

    public static string TenantAccountTimeIndex(string tableName) => $"ix_{tableName}_tenant_account_time";

    public static string CorrelationIndex(string tableName) => $"ix_{tableName}_correlation";

    /// <summary>
    /// Refuses a table name whose longest derived primary-key or index name would exceed 63 bytes. Storage
    /// identifiers are validated as ASCII, so the character count equals the byte count.
    /// </summary>
#nullable disable // keep the builder nullability-agnostic, matching the shared storage identifier validators
    public static IRuleBuilderOptions<T, string> FitsDerivedStorageNames<T>(this IRuleBuilder<T, string> rule)
#nullable restore
    {
        return rule.Must(tableName => tableName is null || Encoding.UTF8.GetByteCount(tableName) <= MaxTableNameLength)
            .WithMessage(
                $"{nameof(AuditLogStorageOptions.TableName)} must be at most {MaxTableNameLength} characters: the audit log "
                    + $"derives index names such as '{TenantAccountTimeIndex("<TableName>")}' from it, and PostgreSQL "
                    + $"truncates identifiers longer than {_MaxIdentifierBytes} bytes, which would make the derived names collide."
            );
    }

    private static int _LongestDerivedNameOverhead()
    {
        string[] names =
        [
            PrimaryKey(""),
            TenantTimeIndex(""),
            TenantActionTimeIndex(""),
            TenantEntityTimeIndex(""),
            TenantActorTimeIndex(""),
            TenantAccountTimeIndex(""),
            CorrelationIndex(""),
        ];

        return names.Max(name => name.Length);
    }
}
