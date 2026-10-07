// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// The relational <see cref="IAuditLogStore"/>: writes audit rows on the saving context's own transaction when its
/// connection belongs to this provider's driver. Otherwise it writes on a separate connection, or throws, as
/// <see cref="AuditLogOptions.MissingTransactionStrategy"/> says.
/// </summary>
internal sealed class RelationalAuditLogStore(RelationalAuditLogWriter writer, RelationalAuditLogEnlistment enlistment)
    : IAuditLogStore
{
    public IReadOnlyList<IAuditLogStoreEntry> Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var (shared, sharedTx) = enlistment.Resolve(savingContext, savingContext.GetType());
        writer.WriteSync(entries, shared, sharedTx);
        return _Entries(entries.Count);
    }

    public async Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        object savingContext,
        CancellationToken cancellationToken = default
    )
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var (shared, sharedTx) = enlistment.Resolve(savingContext, savingContext.GetType());
        await writer.WriteAsync(entries, shared, sharedTx, cancellationToken).ConfigureAwait(false);
        return _Entries(entries.Count);
    }

    private static IAuditLogStoreEntry[] _Entries(int count)
    {
        return [.. Enumerable.Repeat(NoopAuditLogStoreEntry.Instance, count)];
    }

    private sealed class NoopAuditLogStoreEntry : IAuditLogStoreEntry
    {
        public static readonly NoopAuditLogStoreEntry Instance = new();

        public void DiscardPendingChanges() { }

        public void ReleaseAfterCommit() { }
    }
}
