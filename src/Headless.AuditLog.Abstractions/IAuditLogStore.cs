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
    /// Persists audit entries synchronously using the specified saving context.
    /// Ensures audit entries target the same context that is executing SaveChanges,
    /// which is critical in multi-DbContext applications.
    /// </summary>
    /// <param name="entries">Audit entries to persist.</param>
    /// <param name="savingContext">
    /// The context instance executing SaveChanges (typed as <see cref="object"/>
    /// to keep this package free of the EF Core dependency).
    /// </param>
    /// <returns>Handles for provider entries added to the current persistence context.</returns>
    /// <remarks>
    /// Implementers MUST return a handle for every audit entry added to the persistence context.
    /// Returning an empty list signals that no entries were added; the orchestrator will then skip
    /// the audit-row commit step. Buffering implementations that defer flushing should still return
    /// handles for entries added to the context. If no entries are added, return an empty list.
    /// </remarks>
    IReadOnlyList<IAuditLogStoreEntry> Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext);

    /// <summary>
    /// Persists audit entries asynchronously using the specified saving context.
    /// </summary>
    /// <param name="entries">Audit entries to persist.</param>
    /// <param name="savingContext">
    /// The context instance executing SaveChanges (typed as <see cref="object"/>
    /// to keep this package free of the EF Core dependency).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Handles for provider entries added to the current persistence context.</returns>
    /// <remarks>
    /// Implementers MUST return a handle for every audit entry added to the persistence context.
    /// Returning an empty list signals that no entries were added; the orchestrator will then skip
    /// the audit-row commit step. Buffering implementations that defer flushing should still return
    /// handles for entries added to the context. If no entries are added, return an empty list.
    /// </remarks>
    Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        object savingContext,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Cleans up audit entries from a prior failed attempt before an execution strategy retry.
    /// The default implementation is a no-op; providers that add entries to a shared context
    /// (e.g., EF Core) should detach stale entries to prevent duplicates.
    /// </summary>
    /// <param name="savingContext">The context instance executing SaveChanges.</param>
    void PrepareForRetry(object savingContext) { }
}

/// <summary>
/// Provider-owned handle for an audit entry added to a persistence context.
/// </summary>
public interface IAuditLogStoreEntry
{
    /// <summary>
    /// Removes the pending audit entry from the provider persistence context.
    /// </summary>
    /// <remarks>
    /// Implementations MUST be idempotent. The orchestrator may call <see cref="Detach"/> more
    /// than once on the same handle (for example during exception unwinding AND from
    /// <c>CompleteSuccessfulSave</c> when <c>acceptAllChangesOnSuccess</c> is <see langword="false"/>).
    /// A no-op on an already-detached entry is the correct behavior.
    /// </remarks>
    void Detach();
}
