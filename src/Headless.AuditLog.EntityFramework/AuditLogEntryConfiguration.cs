// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.AuditLog;

internal sealed class AuditLogEntryConfiguration(string? schema, string tableName, string? jsonColumnType)
    : IEntityTypeConfiguration<AuditLogEntry>
{
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable(tableName, schema);

        // Composite PK for partition-readiness (time-range partitioning by CreatedAt).
        // Note: SQLite does not support autoincrement on composite keys. Consumers
        // targeting SQLite must override the key configuration (e.g. single-column PK on Id).
        builder.HasKey(e => new { e.CreatedAt, e.Id });

        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Action).IsRequired().HasMaxLength(256);
        builder.Property(e => e.EntityType).HasMaxLength(512);
        builder.Property(e => e.EntityId).HasMaxLength(256);
        builder.Property(e => e.UserId).HasMaxLength(128);
        builder.Property(e => e.AccountId).HasMaxLength(128);
        builder.Property(e => e.TenantId).HasMaxLength(128);
        builder.Property(e => e.IpAddress).HasMaxLength(45); // IPv6 max length
        builder.Property(e => e.UserAgent).HasMaxLength(512);
        builder.Property(e => e.CorrelationId).HasMaxLength(128);
        builder.Property(e => e.ErrorCode).HasMaxLength(256);

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

        // Optional: override to native JSON column type (e.g., "jsonb" for PostgreSQL)
        if (jsonColumnType is not null)
        {
            builder.Property(e => e.OldValues).HasColumnType(jsonColumnType);
            builder.Property(e => e.NewValues).HasColumnType(jsonColumnType);
            builder.Property(e => e.ChangedFields).HasColumnType(jsonColumnType);
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
