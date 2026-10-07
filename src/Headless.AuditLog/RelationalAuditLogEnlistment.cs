// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

/// <summary>
/// Finds the caller's connection and transaction for a relational audit write, and applies
/// <see cref="AuditLogOptions.MissingTransactionStrategy"/> when there is none this provider can join.
/// </summary>
internal sealed partial class RelationalAuditLogEnlistment(
    RelationalAuditLogTable table,
    IOptions<AuditLogOptions> options,
    IAmbientDbTransactionAccessor? ambientTransactionAccessor = null,
    ILogger<RelationalAuditLogEnlistment>? logger = null
)
{
    // Process-wide dedup keyed on the unexpected connection's type name. Logs each distinct
    // mismatch shape once — multi-tenant or multi-store deployments with different misconfigs
    // each surface their own warning rather than the first mismatch silencing all others.
    private static readonly ConcurrentDictionary<string, byte> _WarnedConnectionTypes = new(StringComparer.Ordinal);

    // Process-wide dedup keyed on the context type name for the "no ambient transaction" path.
    // Fires once per distinct context shape where the caller never opened a transaction — audit rows
    // then commit on a separate connection and are NOT atomic with the caller's work.
    private static readonly ConcurrentDictionary<string, byte> _WarnedMissingTransactionContexts = new(
        StringComparer.Ordinal
    );

    private readonly ILogger<RelationalAuditLogEnlistment> _logger =
        logger ?? NullLogger<RelationalAuditLogEnlistment>.Instance;

    /// <summary>
    /// Returns the caller's connection and transaction when <paramref name="context"/> has an active one on this
    /// provider's driver. Otherwise returns <c>(null, null)</c> so the writer opens its own connection, or throws when
    /// the strategy is <see cref="MissingTransactionStrategy.Throw"/>.
    /// </summary>
    /// <param name="context">The context whose transaction the rows must join, or <see langword="null"/> when none is available.</param>
    /// <param name="contextType">The context type, named in the warning and the exception.</param>
    /// <exception cref="InvalidOperationException">
    /// The strategy is <see cref="MissingTransactionStrategy.Throw"/> and no joinable transaction exists.
    /// </exception>
    public (DbConnection? Connection, DbTransaction? Transaction) Resolve(object? context, Type contextType)
    {
        var strategy = options.Value.MissingTransactionStrategy;
        var contextTypeName = contextType.FullName ?? contextType.Name;

        if (ambientTransactionAccessor is null)
        {
            if (strategy == MissingTransactionStrategy.Throw)
            {
                throw new InvalidOperationException(
                    $"{table.Dialect.DisplayName} audit log store cannot enroll in the transaction of {contextTypeName} "
                        + $"because no {nameof(IAmbientDbTransactionAccessor)} is registered, and "
                        + $"{nameof(AuditLogOptions.MissingTransactionStrategy)} is {nameof(MissingTransactionStrategy.Throw)}. "
                        + "Register the Headless EF Core services (AddHeadlessDbContext) or an accessor of your own."
                );
            }

            return (null, null);
        }

        var (connection, transaction) = context is null ? (null, null) : ambientTransactionAccessor.TryResolve(context);

        if (connection is null || transaction is null)
        {
            if (strategy == MissingTransactionStrategy.Throw)
            {
                throw new InvalidOperationException(
                    $"{table.Dialect.DisplayName} audit log store cannot write outside the caller's transaction: "
                        + $"{contextTypeName} has no active transaction, and "
                        + $"{nameof(AuditLogOptions.MissingTransactionStrategy)} is {nameof(MissingTransactionStrategy.Throw)}. "
                        + "Begin a transaction or unit of work on the context before writing."
                );
            }

            if (_WarnedMissingTransactionContexts.TryAdd(contextTypeName, 0))
            {
                LogProviderMissingAmbientTransaction(_logger, table.Dialect.DisplayName, contextTypeName);
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

        var connectionTypeName = connection.GetType().FullName ?? "(unknown)";

        if (strategy == MissingTransactionStrategy.Throw)
        {
            throw new InvalidOperationException(
                $"{table.Dialect.DisplayName} audit log store cannot enroll in the transaction of {contextTypeName} "
                    + $"because its connection is {connectionTypeName}, not {table.Dialect.ConnectionType.Name}, and "
                    + $"{nameof(AuditLogOptions.MissingTransactionStrategy)} is {nameof(MissingTransactionStrategy.Throw)}."
            );
        }

        // Provider mismatch: fall back to our own connection. Log once per distinct mismatch shape — the store
        // is scoped (per-request), so per-instance dedup would flood logs; a flat static flag would silently
        // swallow unrelated misconfigs in multi-tenant or multi-store hosts.
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
        Message = "{Engine} audit log store could not enroll in an ambient transaction for context {SavingContextType} because the consumer did not open one (e.g. no BeginTransaction call). Audit rows will commit on a separate connection BEFORE the consumer's work and are NOT atomic with it — a failed save will leave orphan audit rows. Subsequent occurrences for this context type are suppressed for the remainder of this process."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogProviderMissingAmbientTransaction(
        ILogger logger,
        string engine,
        string savingContextType
    );
}
