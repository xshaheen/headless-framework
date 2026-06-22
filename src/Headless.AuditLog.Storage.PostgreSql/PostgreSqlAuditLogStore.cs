// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
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

    // Process-wide dedup keyed on the saving-context type name for the "no ambient transaction"
    // path. Fires once per distinct DbContext shape where the consumer never opened an explicit
    // transaction — audit rows then commit on a separate connection and are NOT atomic with the
    // consumer's SaveChanges, so an entity-save failure leaves orphan audit rows.
    private static readonly ConcurrentDictionary<string, byte> _WarnedMissingTransactionContexts = new(
        StringComparer.Ordinal
    );

    private readonly ILogger<PostgreSqlAuditLogStore> _logger = logger ?? NullLogger<PostgreSqlAuditLogStore>.Instance;

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
            // Consumer's DbContext has no ambient transaction — typically because BeginTransaction
            // was never called on the SaveChanges path. Audit rows will commit on a separate
            // connection BEFORE the consumer's SaveChanges, so an entity-save failure leaves
            // orphan audit rows. Log once per distinct saving-context shape.
            var savingContextTypeName = savingContext.GetType().FullName ?? "(unknown)";
            if (_WarnedMissingTransactionContexts.TryAdd(savingContextTypeName, 0))
            {
                LogProviderMissingAmbientTransaction(_logger, savingContextTypeName);
            }

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

    private static IAuditLogStoreEntry[] _Entries(int count)
    {
        return count == 0 ? [] : [.. Enumerable.Repeat(NoopAuditLogStoreEntry.Instance, count)];
    }

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
    // ReSharper disable once InconsistentNaming
    private static partial void LogProviderMismatch(ILogger logger, string connectionType);

    [LoggerMessage(
        EventId = 2,
        EventName = "PostgreSqlAuditLogProviderMissingAmbientTransaction",
        Level = LogLevel.Warning,
        Message = "PostgreSql audit log store could not enroll in an ambient transaction for saving context {SavingContextType} because the consumer did not open one (e.g. no BeginTransaction call). Audit rows will commit on a separate connection BEFORE the consumer's SaveChanges and are NOT atomic with it — an entity-save failure will leave orphan audit rows. Subsequent occurrences for this saving-context type are suppressed for the remainder of this process."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogProviderMissingAmbientTransaction(ILogger logger, string savingContextType);
}
