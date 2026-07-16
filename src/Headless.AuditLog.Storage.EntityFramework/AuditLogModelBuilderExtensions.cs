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
    /// <param name="options">Audit log storage options.</param>
    /// <remarks>
    /// This method is idempotent. If the audit log entity is already configured,
    /// subsequent calls are no-ops.
    /// </remarks>
    public static ModelBuilder AddHeadlessAuditLog(this ModelBuilder modelBuilder, AuditLogStorageOptions options)
    {
        if (modelBuilder.Model.FindAnnotation(AuditLogStorageModelAnnotations.IsConfigured)?.Value is true)
        {
            return modelBuilder;
        }

        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration(options));
        modelBuilder.Model.SetAnnotation(AuditLogStorageModelAnnotations.IsConfigured, true);
        return modelBuilder;
    }
}
