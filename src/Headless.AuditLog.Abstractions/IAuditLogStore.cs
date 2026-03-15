// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Storage abstraction for persisting audit log entries.
/// The EF Core implementation adds entries to the existing <c>DbContext</c>
/// so they commit atomically with entity changes.
/// </summary>
public interface IAuditLogStore
{
    /// <summary>
    /// Persists audit entries synchronously.
    /// Called from the synchronous <c>SaveChanges</c> path.
    /// </summary>
    void Save(IReadOnlyList<AuditLogEntryData> entries);

    /// <summary>
    /// Persists audit entries asynchronously.
    /// Called from the asynchronous <c>SaveChangesAsync</c> path.
    /// </summary>
    Task SaveAsync(IReadOnlyList<AuditLogEntryData> entries, CancellationToken cancellationToken = default);
}
