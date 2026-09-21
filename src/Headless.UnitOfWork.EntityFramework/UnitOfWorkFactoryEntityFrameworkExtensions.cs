// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// EF Core entry points for the unit of work: <c>BeginAsync(db)</c> (owned mode — the unit begins the
/// transaction eagerly and owns commit), <c>Enlist(db, transaction)</c> (observed mode — the caller commits),
/// and <c>RunAsync(db, …)</c> (execution-strategy-safe block). Every misuse fails with a message that names the
/// remedy.
/// </summary>
/// <remarks>
/// Begin, enlist, and run bind the unit to the context — and to the connection beneath it — which is how the
/// save pipeline, a domain-event handler, a raw-ADO helper, and anything else holding the context reach the
/// unit that owns its transaction (<see cref="HeadlessDbContextUnitOfWorkExtensions.UnitOfWork" />). The binding
/// hides terminal units automatically, and evicts an owned unit whose transaction ended without going through the
/// unit (a pooled context reset, a transaction disposed by hand). <c>RunAsync(db, …)</c> on a context that already
/// carries a live unit joins it: the block runs inside the owner's unit, outside any execution strategy of its own,
/// and neither commits nor rolls back, so a service that wraps its own work in <c>RunAsync</c> composes under a
/// caller that already opened the transaction; a joined block that completes or rolls the unit back itself is
/// refused once it returns. A second <c>BeginAsync</c> or <c>Enlist</c> on a bound context is refused instead,
/// because an owning handle over someone else's transaction has no honest semantics, and so is any EF entry point
/// on a context whose connection a raw-ADO unit owns, which an EF block cannot join.
/// </remarks>
[PublicAPI]
public static class UnitOfWorkFactoryEntityFrameworkExtensions
{
    private const string _ExistingTransactionMessage =
        "The DbContext already has an active transaction. Begin the unit of work before beginning the transaction, or call IUnitOfWorkFactory.Enlist(db, transaction) for a transaction you commit yourself.";

    extension(IUnitOfWorkFactory factory)
    {
        /// <summary>
        /// Begins an owned unit of work on <paramref name="db" />: the transaction is started on this line
        /// and <c>CompleteAsync</c> commits it, then drains.
        /// </summary>
        /// <param name="db">The context whose database transaction the unit owns.</param>
        /// <param name="isolation">Transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted" />.</param>
        /// <param name="cancellationToken">Propagates the caller's cancellation to the transaction begin.</param>
        /// <returns>The begun unit of work; the caller completes or disposes it.</returns>
        /// <exception cref="InvalidOperationException">
        /// The context or its connection already carries an active unit of work (join it with <c>RunAsync</c>
        /// or pass it along), the context already has an active transaction (use <c>Enlist</c> instead), or the
        /// configured execution strategy retries (use <c>RunAsync</c> instead).
        /// </exception>
        public ValueTask<IUnitOfWork> BeginAsync(
            DbContext db,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            return _BeginAsync(factory, db, isolation, rejectRetryingStrategy: true, cancellationToken);
        }

        /// <summary>
        /// Enlists an already-open EF transaction in observed mode: the caller commits the transaction and
        /// then calls <c>CompleteAsync</c> (which drains without committing). This is the advanced seam for
        /// code that owns its commit edge (the save pipeline, the inbox runners).
        /// </summary>
        /// <param name="db">The context owning <paramref name="transaction" />.</param>
        /// <param name="transaction">The open transaction the unit observes.</param>
        /// <returns>The enlisted unit of work.</returns>
        /// <exception cref="InvalidOperationException">
        /// The context already carries an active unit of work (join it with <c>RunAsync</c> or pass it along), or
        /// its connection is owned by a raw-ADO unit that an EF unit cannot observe.
        /// </exception>
        public IUnitOfWork Enlist(DbContext db, IDbContextTransaction transaction)
        {
            Argument.IsNotNull(db);
            Argument.IsNotNull(transaction);

            DbContextUnitOfWorkBinding.ThrowIfBound(db);

            var unit = factory.Enlist(new EfUnitOfWorkResource(db, transaction, owned: false));

            DbContextUnitOfWorkBinding.Bind(db, unit);

            return unit;
        }

        /// <summary>
        /// Runs <paramref name="operation" /> as a unit of work inside <paramref name="db" />'s execution
        /// strategy: begin (owned) → operation → <c>CompleteAsync</c>. A retriable failure before the commit
        /// starts replays the whole block with a fresh transaction and a fresh unit; once the commit has
        /// started, or after <see cref="IUnitOfWork.PreventRetry" />, the fault is surfaced outside the
        /// strategy so EF cannot replay a possibly-committed block. A drain fault after a durable commit is
        /// logged, never surfaced (the same policy as the Npgsql and SqlClient <c>RunAsync</c>). When the
        /// context already carries a live unit, the block joins it instead: it receives that unit, runs outside
        /// any execution strategy of its own, and commit or rollback stay with the owner; a block that ends the
        /// unit itself is refused once it returns.
        /// </summary>
        /// <param name="db">The context to operate on.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token.</param>
        /// <param name="isolation">
        /// Transaction isolation level for a unit this call begins. Defaults to <see cref="IsolationLevel.ReadCommitted" />.
        /// Ignored when the call joins an already-bound unit: the block runs at the owner's isolation level.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, and the operation.</param>
        /// <exception cref="InvalidOperationException">
        /// The context's connection is owned by a raw-ADO unit (begin the EF unit first and join it from the ADO
        /// side), or a joined block completed, rolled back, or disposed the owner's unit.
        /// </exception>
        public Task RunAsync(
            DbContext db,
            Func<IUnitOfWork, CancellationToken, Task> operation,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(operation);

            return _RunCoreAsync(
                factory,
                db,
                async (unitOfWork, ct) =>
                {
                    await operation(unitOfWork, ct).ConfigureAwait(false);

                    return true;
                },
                isolation,
                cancellationToken
            );
        }

        /// <summary>
        /// Runs <paramref name="operation" /> as a unit of work inside <paramref name="db" />'s execution
        /// strategy and returns its result, with the same replay semantics as the result-less
        /// <c>RunAsync</c> overload.
        /// </summary>
        /// <typeparam name="TResult">Type of the value returned by <paramref name="operation" />.</typeparam>
        /// <param name="db">The context to operate on.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token, returning a result.</param>
        /// <param name="isolation">
        /// Transaction isolation level for a unit this call begins. Defaults to <see cref="IsolationLevel.ReadCommitted" />.
        /// Ignored when the call joins an already-bound unit: the block runs at the owner's isolation level.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, and the operation.</param>
        /// <exception cref="InvalidOperationException">
        /// The context's connection is owned by a raw-ADO unit (begin the EF unit first and join it from the ADO
        /// side), or a joined block completed, rolled back, or disposed the owner's unit.
        /// </exception>
        public Task<TResult> RunAsync<TResult>(
            DbContext db,
            Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(operation);

            return _RunCoreAsync(factory, db, operation, isolation, cancellationToken);
        }
    }

    private static async ValueTask<IUnitOfWork> _BeginAsync(
        IUnitOfWorkFactory factory,
        DbContext db,
        IsolationLevel isolation,
        bool rejectRetryingStrategy,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(db);

        var unit = await factory
            .BeginAsync(
                async ct =>
                {
                    // An owning begin on a context (or connection) that already carries a live unit is refused:
                    // RunAsync is the join, and a second transaction on the same context is never the answer.
                    DbContextUnitOfWorkBinding.ThrowIfBound(db);

                    if (db.Database.CurrentTransaction is not null)
                    {
                        throw new InvalidOperationException(_ExistingTransactionMessage);
                    }

                    if (rejectRetryingStrategy)
                    {
                        var strategy = db.Database.CreateExecutionStrategy();

                        if (strategy.RetriesOnFailure)
                        {
                            throw new InvalidOperationException(
                                $"The configured execution strategy '{strategy.GetType().FullName}' does not support user-initiated transactions. "
                                    + "Use the execution strategy returned by 'Database.CreateExecutionStrategy()' to execute all the operations in the transaction as a retriable unit."
                                    + " Use IUnitOfWorkFactory.RunAsync(db, …) to run the unit of work as a retriable block."
                            );
                        }
                    }

                    var transaction = await db.Database.BeginTransactionAsync(isolation, ct).ConfigureAwait(false);

                    return new EfUnitOfWorkResource(db, transaction, owned: true);
                },
                options: null,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        DbContextUnitOfWorkBinding.Bind(db, unit);

        return unit;
    }

    private static async Task<TResult> _RunCoreAsync<TResult>(
        IUnitOfWorkFactory factory,
        DbContext db,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(db);

        // A joined block belongs to the owner's unit and the owner's execution strategy: it runs inline, a fault
        // propagates to the owner's block, which is what unwinds the unit, and a block that ends the unit itself
        // is refused once it returns.
        if (DbContextUnitOfWorkBinding.TryGet(db, out var joined))
        {
            return await UnitOfWorkRunner.RunJoinedAsync(joined, operation, cancellationToken).ConfigureAwait(false);
        }

        // Refused here, before the strategy, so the caller sees the EF-specific remedy: the connection-owned unit
        // is a raw-ADO one, and an EF block cannot join it (the context would begin a second transaction on the
        // connection the ADO unit already owns).
        DbContextUnitOfWorkBinding.ThrowIfConnectionBound(db);

        var logger = UnitOfWorkRunner.LoggerFor(factory);
        var state = (Factory: factory, Operation: operation, Isolation: isolation, Context: db, Logger: logger);

        var (result, error) = await db
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                async (state, ct) =>
                {
                    var unitOfWork = default(IUnitOfWork);
                    var commitStarted = false;
                    var result = default(TResult)!;

                    try
                    {
                        // Retries are legal here: this begin runs inside the strategy, so the retrying check
                        // is suppressed and a transient failure replays with a fresh unit and transaction.
                        unitOfWork = await _BeginAsync(
                                state.Factory,
                                state.Context,
                                state.Isolation,
                                rejectRetryingStrategy: false,
                                ct
                            )
                            .ConfigureAwait(false);

                        result = await state.Operation(unitOfWork, ct).ConfigureAwait(false);
                        commitStarted = true;

                        try
                        {
                            await unitOfWork.CompleteAsync(ct).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (unitOfWork.State == UnitOfWorkState.Completed)
                        {
                            // The transaction is ALREADY durably committed; only the drain faulted. Same policy as
                            // the Npgsql/SqlClient RunAsync: log and return the committed result, because surfacing
                            // it would invite a retry that double-applies a committed block.
                            UnitOfWorkRunner.LogPostCommitDrainFaulted(state.Logger, ex);
                        }

                        return (Result: result, Error: null!);
                    }
                    catch (Exception ex)
                    {
                        // Once the commit has started (it may have committed before the fault) or the block
                        // marked itself non-replayable, the fault must NOT reach the strategy's retry loop:
                        // it is captured and rethrown after ExecuteAsync returns, outside the strategy.
                        if (commitStarted || unitOfWork?.IsRetryPrevented == true)
                        {
                            await _DisposeQuietlyAsync(unitOfWork, state.Logger).ConfigureAwait(false);

                            return (Result: result, Error: ExceptionDispatchInfo.Capture(ex));
                        }

                        // A pre-commit failure replays: unwind this attempt's unit first — its rollback must
                        // finish before the strategy re-runs, or the replay's BeginAsync meets a still-open
                        // transaction on the same context.
                        await _DisposeQuietlyAsync(unitOfWork, state.Logger).ConfigureAwait(false);

                        throw;
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        error?.Throw();

        return result;
    }

    private static async ValueTask _DisposeQuietlyAsync(IUnitOfWork? unitOfWork, ILogger logger)
    {
        if (unitOfWork is null)
        {
            return;
        }

        // Terminal units (a completed or already-failed commit) make this a no-op; an active unit is
        // abandoned, which rolls its transaction back and drops its registrations.
        try
        {
            await unitOfWork.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The captured/rethrown fault is the caller's outcome; a dispose fault must not mask it, but it is
            // still a real secondary failure, so it is logged rather than dropped.
            UnitOfWorkRunner.LogAttemptDisposeFaulted(logger, ex);
        }
    }
}
