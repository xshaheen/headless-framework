// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

/// <summary>ModelBuilder extensions for configuring the audit log schema.</summary>
[PublicAPI]
public static class AuditLogModelBuilderExtensions
{
    /// <summary>
    /// Registers and configures the <see cref="AuditLogEntry"/> entity type.
    /// Call this from your <c>DbContext.OnModelCreating</c>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="tableName">Table name. Default: <c>"audit_log"</c>.</param>
    /// <param name="schema">Optional database schema.</param>
    /// <param name="jsonColumnType">
    /// Optional native JSON column type override (e.g., <c>"jsonb"</c> for PostgreSQL,
    /// <c>"json"</c> for SQL Server 2025+). When <c>null</c>, string columns with
    /// value converters are used for universal portability.
    /// </param>
    /// <remarks>
    /// This method is idempotent. If the audit log entity is already configured,
    /// subsequent calls are no-ops.
    /// </remarks>
    public static ModelBuilder ConfigureAuditLog(
        this ModelBuilder modelBuilder,
        string tableName = "audit_log",
        string? schema = null,
        string? jsonColumnType = null
    )
    {
        // Idempotent: skip if already configured
        if (modelBuilder.Model.FindEntityType(typeof(AuditLogEntry)) is not null)
        {
            return modelBuilder;
        }

        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration(schema, tableName, jsonColumnType));
        return modelBuilder;
    }
}
