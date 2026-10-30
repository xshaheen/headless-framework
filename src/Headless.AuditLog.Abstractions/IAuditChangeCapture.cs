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
    /// <param name="context">The actor, client, correlation, and timestamp to stamp every entry with.</param>
    IReadOnlyList<AuditLogEntryData> CaptureChanges(IEnumerable<object> entries, AuditCaptureContext context);
}
