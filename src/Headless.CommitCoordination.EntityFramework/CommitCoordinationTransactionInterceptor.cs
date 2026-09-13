// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Checks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// EF Core <see cref="DbTransactionInterceptor" /> that signals the commit coordination scope enlisted for a
/// transaction when that transaction commits or rolls back.
/// </summary>
/// <remarks>
/// The interceptor owns the transaction-to-scope map. <see cref="Enlist" /> (reached through
/// <c>DatabaseFacade.EnlistCommitCoordination</c>) opens the scope and registers it under the live
/// <see cref="DbTransaction" />; the returned scope removes the entry when it is disposed, so eviction never
/// depends on an interceptor event firing — a commit that throws client-side but is later probe-confirmed by the
/// caller raises no interceptor event, and the caller signals the scope directly instead.
/// <para>
/// The synchronous overrides (<see cref="TransactionCommitted" />, <see cref="TransactionRolledBack" />) claim the
/// outcome on the committing thread and run the drain off-thread (fire-and-forget) so the EF call does not block
/// on callbacks. The async overrides await the drain. Because signals are idempotent per outcome, a caller that
/// also signals the scope explicitly (the inbox transaction runners do) drains once with no warning.
/// </para>
/// <para>
/// Drain faults are logged and swallowed: by the time these methods fire, the transaction outcome is already
/// durable. Propagating a drain fault would surface a phantom failure to the caller (and, inside an EF execution
/// strategy, cause the operation to be retried and double-applied). The enlisted durable rows are
/// relay-recoverable. Transactions that were never enlisted are ignored.
/// </para>
/// </remarks>
internal sealed partial class CommitCoordinationTransactionInterceptor(
    ILogger<CommitCoordinationTransactionInterceptor>? logger = null
) : DbTransactionInterceptor
{
    private readonly ILogger _logger = logger ?? NullLogger<CommitCoordinationTransactionInterceptor>.Instance;
    private readonly ConcurrentDictionary<DbTransaction, ICommitScope> _scopes = new(
        ReferenceEqualityComparer.Instance
    );

    /// <summary>Gets the number of transactions currently enlisted. Exposed for tests.</summary>
    internal int EnlistedTransactionCount => _scopes.Count;

    /// <summary>
    /// Opens a scope for <paramref name="transaction" /> and registers it so the interceptor can signal it on the
    /// transaction's commit or rollback edge. The returned scope evicts the entry when disposed.
    /// </summary>
    /// <param name="scopeFactory">The core scope factory.</param>
    /// <param name="relational">The live connection/transaction handle exposed to participants.</param>
    /// <param name="transaction">The provider transaction the interceptor will receive on the commit edge.</param>
    /// <returns>The scope the caller owns: signal it (optional — the interceptor does) and dispose it after the transaction completes.</returns>
    /// <exception cref="InvalidOperationException">A scope is already enlisted for <paramref name="transaction" />.</exception>
    internal ICommitScope Enlist(
        ICommitScopeFactory scopeFactory,
        IRelationalCommitContext relational,
        DbTransaction transaction
    )
    {
        Argument.IsNotNull(scopeFactory);
        Argument.IsNotNull(relational);
        Argument.IsNotNull(transaction);

        // Open first: the ambient push must land in the caller's frame, and the factory is the only thing that can
        // create a scope. On a duplicate the fresh scope is disposed immediately — an un-signalled dispose of an
        // empty scope just pops the ambient frame it pushed.
        var scope = scopeFactory.Open(relational);

        if (!_scopes.TryAdd(transaction, scope))
        {
            scope.Dispose();
            LogDuplicateEnlistment(_logger);

            throw new InvalidOperationException(
                "An EF Core commit coordination scope is already enlisted for this transaction."
            );
        }

        return new EnlistedCommitScope(this, transaction, scope);
    }

    /// <inheritdoc />
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        _SignalInBackground(transaction, CommitOutcome.Committed);
    }

    /// <inheritdoc />
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        if (!_scopes.TryGetValue(transaction, out var scope))
        {
            return;
        }

        try
        {
            await scope.SignalAsync(CommitOutcome.Committed).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogDrainFaulted(_logger, CommitOutcome.Committed, ex);
        }
    }

    /// <inheritdoc />
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        _SignalInBackground(transaction, CommitOutcome.RolledBack);
    }

    /// <inheritdoc />
    public override async Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        if (!_scopes.TryGetValue(transaction, out var scope))
        {
            return;
        }

        try
        {
            await scope.SignalAsync(CommitOutcome.RolledBack).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogDrainFaulted(_logger, CommitOutcome.RolledBack, ex);
        }
    }

    private void _SignalInBackground(DbTransaction transaction, CommitOutcome outcome)
    {
        if (!_scopes.TryGetValue(transaction, out var scope))
        {
            return;
        }

        // Invoke SignalAsync synchronously (NOT inside Task.Run): the terminal claim settles on the committing thread
        // before this returns, so the caller's un-signalled scope dispose that follows cannot race it into a rollback.
        // Only the awaited drain continues off-thread. There is no shutdown drain gate — the drain is acceleration,
        // not durability: the rows were committed in the transaction and the relay sweep recovers an abandoned drain.
        var signal = scope.SignalAsync(outcome);

        if (signal.IsCompletedSuccessfully)
        {
            return;
        }

        BackgroundFault.Observe(
            signal.AsTask(),
            (_logger, outcome),
            static (state, exception) => LogDrainFaulted(state._logger, state.outcome, exception)
        );
    }

    private void _Evict(DbTransaction transaction, ICommitScope scope)
    {
        // Remove-if-equal: never evict a successor that re-enlisted the same transaction after this scope was
        // removed (cannot happen while this scope is live, but the guard costs nothing).
        _scopes.TryRemove(new KeyValuePair<DbTransaction, ICommitScope>(transaction, scope));
    }

    /// <summary>
    /// The scope handed to the enlisting caller: delegates to the core scope and evicts the interceptor's map
    /// entry on disposal. Disposal stays synchronous so the ambient pop runs in the caller's own frame.
    /// </summary>
    private sealed class EnlistedCommitScope(
        CommitCoordinationTransactionInterceptor owner,
        DbTransaction transaction,
        ICommitScope inner
    ) : ICommitScope
    {
        public ICommitCoordinator Coordinator => inner.Coordinator;

        public ValueTask SignalAsync(CommitOutcome outcome)
        {
            return inner.SignalAsync(outcome);
        }

        public void Dispose()
        {
            owner._Evict(transaction, inner);
            inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            owner._Evict(transaction, inner);

            return inner.DisposeAsync();
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "An EF Core commit coordination scope is already enlisted for this transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDuplicateEnlistment(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "EF Core commit coordination drain faulted after a durable {Outcome} edge; the relay will recover any committed work."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDrainFaulted(ILogger logger, CommitOutcome outcome, Exception exception);
}
