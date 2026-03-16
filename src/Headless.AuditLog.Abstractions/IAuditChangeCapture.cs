// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Scans ChangeTracker entries and produces audit log entry data.
/// Internal contract between the entity processor and the audit subsystem.
/// The <c>entries</c> parameter accepts <see cref="object"/> to keep this
/// package free of the EF Core dependency; the EF implementation casts internally.
/// </summary>
public interface IAuditChangeCapture
{
    /// <summary>
    /// Captures changes from the provided ChangeTracker entries.
    /// </summary>
    /// <param name="entries">EF Core <c>EntityEntry</c> objects (typed as <see cref="object"/> to avoid EF dependency).</param>
    /// <param name="userId">Current user's ID string.</param>
    /// <param name="accountId">Current user's account ID string.</param>
    /// <param name="tenantId">Current tenant ID.</param>
    /// <param name="correlationId">Correlation ID for this operation.</param>
    /// <param name="timestamp">UTC timestamp to stamp all entries with.</param>
    IReadOnlyList<AuditLogEntryData> CaptureChanges(
        IEnumerable<object> entries,
        string? userId,
        string? accountId,
        string? tenantId,
        string? correlationId,
        DateTimeOffset timestamp
    );
}
