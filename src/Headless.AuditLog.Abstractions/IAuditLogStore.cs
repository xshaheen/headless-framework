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

    /// <summary>
    /// Persists audit entries synchronously using the specified saving context.
    /// Ensures audit entries target the same context that is executing SaveChanges,
    /// which is critical in multi-DbContext applications.
    /// </summary>
    /// <param name="entries">Audit entries to persist.</param>
    /// <param name="savingContext">
    /// The context instance executing SaveChanges (typed as <see cref="object"/>
    /// to keep this package free of the EF Core dependency).
    /// </param>
    void Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext) => Save(entries);

    /// <summary>
    /// Persists audit entries asynchronously using the specified saving context.
    /// </summary>
    Task SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        object savingContext,
        CancellationToken cancellationToken = default
    ) => SaveAsync(entries, cancellationToken);

    /// <summary>
    /// Cleans up audit entries from a prior failed attempt before an execution strategy retry.
    /// The default implementation is a no-op; providers that add entries to a shared context
    /// (e.g., EF Core) should detach stale entries to prevent duplicates.
    /// </summary>
    /// <param name="savingContext">The context instance executing SaveChanges.</param>
    void PrepareForRetry(object savingContext) { }
}
