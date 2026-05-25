// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.AuditLog.SqlServer;

internal sealed partial class SqlServerAuditLogStore(
    SqlServerAuditLogWriter writer,
    IAmbientDbTransactionAccessor? ambientTransactionAccessor = null,
    ILogger<SqlServerAuditLogStore>? logger = null
) : IAuditLogStore
{
    private static int _MismatchWarned;

    private readonly ILogger<SqlServerAuditLogStore> _logger =
        logger ?? NullLogger<SqlServerAuditLogStore>.Instance;

    public IReadOnlyList<IAuditLogStoreEntry> Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext)
    {
        var (shared, sharedTx) = _TryResolveShared(savingContext);
        writer.WriteSync(entries, shared, sharedTx);
        return _Entries(entries.Count);
    }

    public async Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        object savingContext,
        CancellationToken cancellationToken = default
    )
    {
        var (shared, sharedTx) = _TryResolveShared(savingContext);
        await writer.WriteAsync(entries, shared, sharedTx, cancellationToken).ConfigureAwait(false);
        return _Entries(entries.Count);
    }

    private (SqlConnection? Connection, SqlTransaction? Transaction) _TryResolveShared(object savingContext)
    {
        if (ambientTransactionAccessor is null)
        {
            return (null, null);
        }

        var (connection, transaction) = ambientTransactionAccessor.TryResolve(savingContext);

        if (connection is null || transaction is null)
        {
            return (null, null);
        }

        if (connection is SqlConnection sqlConn && transaction is SqlTransaction sqlTx)
        {
            return (sqlConn, sqlTx);
        }

        // Provider mismatch: the consumer's DbContext is using a different driver (e.g. Npgsql).
        // Fall back to opening our own connection. Log once per process — the store is registered
        // scoped, so a per-instance flag would still fire once per request.
        if (Interlocked.Exchange(ref _MismatchWarned, 1) == 0)
        {
            LogProviderMismatch(_logger, connection.GetType().FullName ?? "(unknown)");
        }

        return (null, null);
    }

    private static IReadOnlyList<IAuditLogStoreEntry> _Entries(int count) =>
        count == 0 ? [] : Enumerable.Repeat(NoopAuditLogStoreEntry.Instance, count).ToArray();

    private sealed class NoopAuditLogStoreEntry : IAuditLogStoreEntry
    {
        public static readonly NoopAuditLogStoreEntry Instance = new();

        public void DiscardPendingChanges() { }

        public void ReleaseAfterCommit() { }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "SqlServerAuditLogProviderMismatch",
        Level = LogLevel.Warning,
        Message = "SqlServer audit log store could not enroll in the consumer's ambient transaction because the active connection is {ConnectionType}, not SqlConnection. Audit rows will commit on a separate connection and are NOT atomic with the consumer's SaveChanges. Warning suppressed for the remainder of this process."
    )]
    private static partial void LogProviderMismatch(ILogger logger, string connectionType);
}
