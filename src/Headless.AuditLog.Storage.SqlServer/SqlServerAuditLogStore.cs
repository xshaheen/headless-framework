// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Headless.AuditLog.SqlServer;

internal sealed class SqlServerAuditLogStore(SqlServerAuditLogWriter writer) : IAuditLogStore
{
    public IReadOnlyList<IAuditLogStoreEntry> Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext)
    {
        writer.WriteSync(entries);
        return _Entries(entries.Count);
    }

    public async Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        object savingContext,
        CancellationToken cancellationToken = default
    )
    {
        await writer.WriteAsync(entries, cancellationToken).ConfigureAwait(false);
        return _Entries(entries.Count);
    }

    private static IReadOnlyList<IAuditLogStoreEntry> _Entries(int count) =>
        count == 0 ? [] : Enumerable.Repeat(NoopAuditLogStoreEntry.Instance, count).ToArray();

    private sealed class NoopAuditLogStoreEntry : IAuditLogStoreEntry
    {
        public static readonly NoopAuditLogStoreEntry Instance = new();

        public void DiscardPendingChanges() { }

        public void ReleaseAfterCommit() { }
    }
}
