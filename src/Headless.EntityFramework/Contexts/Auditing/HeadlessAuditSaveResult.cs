// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Headless.EntityFramework;

internal readonly record struct HeadlessAuditSaveResult(
    bool RequiresManualAcceptAllChanges,
    IReadOnlyList<IAuditLogStoreEntry>? AuditEntries
)
{
    /// <summary>The number of audit rows this result added to the saving context.</summary>
    public int Count => AuditEntries?.Count ?? 0;

    public HeadlessAuditSaveResult Merge(HeadlessAuditSaveResult other)
    {
        if (Count == 0 && !RequiresManualAcceptAllChanges)
        {
            return other;
        }

        if (other.Count == 0 && !other.RequiresManualAcceptAllChanges)
        {
            return this;
        }

        return new(
            RequiresManualAcceptAllChanges || other.RequiresManualAcceptAllChanges,
            [.. AuditEntries ?? [], .. other.AuditEntries ?? []]
        );
    }
}

/// <summary>
/// The outcome of adding the audit rows that need no post-save resolution: the rows added, and the captured entries
/// that still need the two-phase flow.
/// </summary>
internal readonly record struct HeadlessAuditSplit(
    HeadlessAuditSaveResult Added,
    IReadOnlyList<AuditLogEntryData>? Deferred
);
