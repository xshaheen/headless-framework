// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework;

/// <summary>
/// Audit-capture and persistence surface used by <see cref="HeadlessSaveChangesPipeline"/>.
/// </summary>
/// <remarks>
/// Framework-internal: exposed as an interface only to allow DI replacement in tests and to keep
/// the pipeline free of <see langword="new"/> constructions. Not part of the public contract.
/// </remarks>
internal interface IHeadlessAuditPersistence
{
    /// <summary>
    /// Captures audit entries from the pre-materialized change-tracker snapshot before <c>SaveChanges</c>.
    /// </summary>
    IReadOnlyList<AuditLogEntryData>? CaptureEntries(
        IReadOnlyList<EntityEntry> entries,
        HeadlessSaveEntryContext saveContext
    );

    /// <summary>Cleans up stale audit entries from a prior failed attempt before an execution strategy retry.</summary>
    void PrepareForRetry(DbContext context);

    /// <summary>
    /// Adds the audit rows that are complete as captured to the context, ahead of the entity save, so they commit in
    /// the same batch. Returns the entries that still need <see cref="ResolveAndPersist"/>.
    /// </summary>
    Task<HeadlessAuditSplit> AddReadyEntriesAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        CancellationToken cancellationToken
    );

    HeadlessAuditSplit AddReadyEntries(DbContext context, IReadOnlyList<AuditLogEntryData>? entries);

    Task<HeadlessAuditSaveResult> ResolveAndPersistAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        CancellationToken cancellationToken
    );

    HeadlessAuditSaveResult ResolveAndPersist(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        Func<bool, int> baseSaveChanges
    );

    void CompleteSuccessfulSave(DbContext context, HeadlessAuditSaveResult auditSave, bool acceptAllChangesOnSuccess);

    void DiscardEntries(HeadlessAuditSaveResult auditSave);

    void ReleaseEntries(HeadlessAuditSaveResult auditSave);
}
