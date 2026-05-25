// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.AuditLog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Headless.AuditLog.PostgreSql;

internal sealed partial class PostgreSqlAuditLogStore(
    PostgreSqlAuditLogWriter writer,
    IAmbientDbTransactionAccessor? ambientTransactionAccessor = null,
    ILogger<PostgreSqlAuditLogStore>? logger = null
) : IAuditLogStore
{
    // Process-wide dedup keyed on the unexpected connection's type name. Logs each distinct
    // mismatch shape once — multi-tenant or multi-store deployments with different misconfigs
    // each surface their own warning rather than the first mismatch silencing all others.
    private static readonly ConcurrentDictionary<string, byte> _WarnedConnectionTypes = new(StringComparer.Ordinal);

    private readonly ILogger<PostgreSqlAuditLogStore> _logger =
        logger ?? NullLogger<PostgreSqlAuditLogStore>.Instance;

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

    private (NpgsqlConnection? Connection, NpgsqlTransaction? Transaction) _TryResolveShared(object savingContext)
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

        if (connection is NpgsqlConnection npgConn && transaction is NpgsqlTransaction npgTx)
        {
            return (npgConn, npgTx);
        }

        // Provider mismatch: the consumer's DbContext is using a different driver (e.g. SqlClient).
        // Fall back to opening our own connection. Log once per distinct mismatch shape — the store
        // is registered scoped (per-request), so per-instance dedup would flood logs; a flat static
        // flag would silently swallow unrelated misconfigs in multi-tenant or multi-store hosts.
        var connectionTypeName = connection.GetType().FullName ?? "(unknown)";
        if (_WarnedConnectionTypes.TryAdd(connectionTypeName, 0))
        {
            LogProviderMismatch(_logger, connectionTypeName);
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
        EventName = "PostgreSqlAuditLogProviderMismatch",
        Level = LogLevel.Warning,
        Message = "PostgreSql audit log store could not enroll in the consumer's ambient transaction because the active connection is {ConnectionType}, not NpgsqlConnection. Audit rows will commit on a separate connection and are NOT atomic with the consumer's SaveChanges. Subsequent occurrences of this exact mismatch are suppressed for the remainder of this process."
    )]
    private static partial void LogProviderMismatch(ILogger logger, string connectionType);
}
