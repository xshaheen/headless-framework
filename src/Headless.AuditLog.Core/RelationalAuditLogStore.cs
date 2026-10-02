// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.AuditLog;

/// <summary>
/// The relational <see cref="IAuditLogStore"/>: writes audit rows on the saving context's own transaction when its
/// connection belongs to this provider's driver, and on a separate connection otherwise.
/// </summary>
internal sealed partial class RelationalAuditLogStore(
    RelationalAuditLogWriter writer,
    RelationalAuditLogTable table,
    IAmbientDbTransactionAccessor? ambientTransactionAccessor = null,
    ILogger<RelationalAuditLogStore>? logger = null
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

    private readonly ILogger<RelationalAuditLogStore> _logger = logger ?? NullLogger<RelationalAuditLogStore>.Instance;

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

    private (DbConnection? Connection, DbTransaction? Transaction) _TryResolveShared(object savingContext)
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
                LogProviderMissingAmbientTransaction(_logger, table.Dialect.DisplayName, savingContextTypeName);
            }

            return (null, null);
        }

        if (
            table.Dialect.ConnectionType.IsInstanceOfType(connection)
            && table.Dialect.TransactionType.IsInstanceOfType(transaction)
        )
        {
            return (connection, transaction);
        }

        // Provider mismatch: the consumer's DbContext is using another database's driver.
        // Fall back to opening our own connection. Log once per distinct mismatch shape — the store
        // is registered scoped (per-request), so per-instance dedup would flood logs; a flat static
        // flag would silently swallow unrelated misconfigs in multi-tenant or multi-store hosts.
        var connectionTypeName = connection.GetType().FullName ?? "(unknown)";
        if (_WarnedConnectionTypes.TryAdd(connectionTypeName, 0))
        {
            LogProviderMismatch(
                _logger,
                table.Dialect.DisplayName,
                connectionTypeName,
                table.Dialect.ConnectionType.Name
            );
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
        EventName = "AuditLogProviderMismatch",
        Level = LogLevel.Warning,
        Message = "{Engine} audit log store could not enroll in the consumer's ambient transaction because the active connection is {ConnectionType}, not {ExpectedConnectionType}. Audit rows will commit on a separate connection and are NOT atomic with the consumer's SaveChanges. Subsequent occurrences of this exact mismatch are suppressed for the remainder of this process."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogProviderMismatch(
        ILogger logger,
        string engine,
        string connectionType,
        string expectedConnectionType
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "AuditLogProviderMissingAmbientTransaction",
        Level = LogLevel.Warning,
        Message = "{Engine} audit log store could not enroll in an ambient transaction for saving context {SavingContextType} because the consumer did not open one (e.g. no BeginTransaction call). Audit rows will commit on a separate connection BEFORE the consumer's SaveChanges and are NOT atomic with it — an entity-save failure will leave orphan audit rows. Subsequent occurrences for this saving-context type are suppressed for the remainder of this process."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogProviderMissingAmbientTransaction(
        ILogger logger,
        string engine,
        string savingContextType
    );
}
