// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.AuditLog;

internal sealed class AuditLogEntryConfiguration(AuditLogStorageOptions options)
    : IEntityTypeConfiguration<AuditLogEntry>
{
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        // The storage row must never recursively produce another automatic audit row.
        builder.ExcludeFromAudit();

        builder.ToTable(options.TableName, options.Schema);

        // Composite PK for partition-readiness (time-range partitioning by CreatedAt).
        // Note: SQLite does not support autoincrement on composite keys. Consumers
        // targeting SQLite must override the key configuration (e.g. single-column PK on Id).
        builder.HasKey(e => new { e.CreatedAt, e.Id });

        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        builder.Property(e => e.Action).IsRequired().HasMaxLength(AuditLogFieldLimits.Action);
        builder.Property(e => e.EntityType).HasMaxLength(AuditLogFieldLimits.EntityType);
        builder.Property(e => e.EntityId).HasMaxLength(AuditLogFieldLimits.EntityId);
        builder.Property(e => e.UserId).HasMaxLength(AuditLogFieldLimits.UserId);
        builder.Property(e => e.AccountId).HasMaxLength(AuditLogFieldLimits.AccountId);
        builder.Property(e => e.TenantId).HasMaxLength(AuditLogFieldLimits.TenantId);
        builder.Property(e => e.IpAddress).HasMaxLength(AuditLogFieldLimits.IpAddress); // IPv6 max length
        builder.Property(e => e.UserAgent).HasMaxLength(AuditLogFieldLimits.UserAgent);
        builder.Property(e => e.CorrelationId).HasMaxLength(AuditLogFieldLimits.CorrelationId);
        builder.Property(e => e.ErrorCode).HasMaxLength(AuditLogFieldLimits.ErrorCode);

        // JSON stored as string columns — universally portable across all DB providers
        // Note: Dictionary<string, object?> round-trips values as JsonElement on read.
        // Consumers must use JsonElement APIs (GetDecimal, GetInt32, etc.) for typed access.
        builder
            .Property(e => e.OldValues)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, _JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(v, _JsonOptions)
            );

        builder
            .Property(e => e.NewValues)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, _JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(v, _JsonOptions)
            );

        builder
            .Property(e => e.ChangedFields)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, _JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, _JsonOptions)
            );

        // Optional: override to native JSON column type (e.g., "jsonb" for PostgreSQL).
        if (options.JsonColumnType is { } jsonColumnType)
        {
            var sqlFragment = jsonColumnType.ToSqlFragment();
            builder.Property(e => e.OldValues).HasColumnType(sqlFragment);
            builder.Property(e => e.NewValues).HasColumnType(sqlFragment);
            builder.Property(e => e.ChangedFields).HasColumnType(sqlFragment);
        }

        // Optional: override the CreatedAt column type (e.g., "timestamp with time zone" for Postgres,
        // "datetime2" for SQL Server). Defaults to the provider's stock mapping when unset.
        if (!string.IsNullOrWhiteSpace(options.CreatedAtColumnType))
        {
            builder.Property(e => e.CreatedAt).HasColumnType(options.CreatedAtColumnType);
        }

        // Indexes optimized for common query patterns
        builder.HasIndex(e => new { e.TenantId, e.CreatedAt }).HasDatabaseName("ix_audit_log_tenant_time");
        builder
            .HasIndex(e => new
            {
                e.TenantId,
                e.Action,
                e.CreatedAt,
            })
            .HasDatabaseName("ix_audit_log_tenant_action_time");
        builder
            .HasIndex(e => new
            {
                e.TenantId,
                e.EntityType,
                e.EntityId,
                e.CreatedAt,
            })
            .HasDatabaseName("ix_audit_log_tenant_entity_time");
        builder
            .HasIndex(e => new
            {
                e.TenantId,
                e.UserId,
                e.CreatedAt,
            })
            .HasDatabaseName("ix_audit_log_tenant_actor_time");
        builder.HasIndex(e => e.CorrelationId).HasDatabaseName("ix_audit_log_correlation");
    }
}
