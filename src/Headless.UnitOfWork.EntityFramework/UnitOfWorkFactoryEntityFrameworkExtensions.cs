// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Polly.Retry;

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
/// refused once it returns. A second context built over the same connection joins the same way: it adopts the
/// unit's transaction on its first lookup, so two <see cref="DbContext" /> types over one database commit
/// atomically in one unit. A second <c>BeginAsync</c> or <c>Enlist</c> on a bound context, or on one whose
/// connection carries a live unit, is refused instead, because an owning handle over someone else's transaction
/// has no honest semantics, and so is any EF entry point on a context whose connection a raw-ADO unit owns, which
/// an EF block cannot join.
/// </remarks>
[PublicAPI]
public static class UnitOfWorkFactoryEntityFrameworkExtensions
{
    private const string _ExistingTransactionMessage =
        "The DbContext already has an active transaction. Begin the unit of work before beginning the transaction, or call IUnitOfWorkFactory.Enlist(db, transaction) for a transaction you commit yourself.";

    extension(IUnitOfWorkFactory factory)
    {
        /// <summary>
        /// Begins an owned unit of work on <paramref name="db" /> at <see cref="IsolationLevel.ReadCommitted" />:
        /// the transaction is started on this line and <c>CompleteAsync</c> commits it, then drains.
        /// </summary>
        /// <param name="db">The context whose database transaction the unit owns.</param>
        /// <param name="cancellationToken">Propagates the caller's cancellation to the transaction begin.</param>
        /// <returns>The begun unit of work; the caller completes or disposes it.</returns>
        /// <exception cref="InvalidOperationException">
        /// The context or its connection already carries an active unit of work (join it with <c>RunAsync</c>
        /// or pass it along), the context already has an active transaction (use <c>Enlist</c> instead), or the
        /// configured execution strategy retries (use <c>RunAsync</c> instead).
        /// </exception>
        public ValueTask<IUnitOfWork> BeginAsync(DbContext db, CancellationToken cancellationToken = default)
        {
            return _BeginAsync(
                factory,
                db,
                IsolationLevel.ReadCommitted,
                rejectRetryingStrategy: true,
                cancellationToken
            );
        }

        /// <summary>
        /// Begins an owned unit of work on <paramref name="db" /> at <paramref name="isolation" />: the transaction
        /// is started on this line and <c>CompleteAsync</c> commits it, then drains.
        /// </summary>
        /// <param name="db">The context whose database transaction the unit owns.</param>
        /// <param name="isolation">Transaction isolation level.</param>
        /// <param name="cancellationToken">Propagates the caller's cancellation to the transaction begin.</param>
        /// <returns>The begun unit of work; the caller completes or disposes it.</returns>
        /// <exception cref="InvalidOperationException">
        /// The context or its connection already carries an active unit of work (join it with <c>RunAsync</c>
        /// or pass it along), the context already has an active transaction (use <c>Enlist</c> instead), or the
        /// configured execution strategy retries (use <c>RunAsync</c> instead).
        /// </exception>
        public ValueTask<IUnitOfWork> BeginAsync(
            DbContext db,
            IsolationLevel isolation,
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
        /// The context or its connection already carries an active unit of work (join it with <c>RunAsync</c> or
        /// pass it along), or its connection is owned by a raw-ADO unit that an EF unit cannot observe.
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
        /// strategy, beginning it at <see cref="IsolationLevel.ReadCommitted" />, with the replay and join semantics
        /// of the overload that takes an <see cref="IsolationLevel" />.
        /// </summary>
        /// <param name="db">The context to operate on.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, and the operation.</param>
        /// <exception cref="InvalidOperationException">
        /// The context's connection is owned by a raw-ADO unit (begin the EF unit first and join it from the ADO
        /// side), the context uses a transaction other than the unit's on that connection, or a joined block
        /// completed, rolled back, or disposed the owner's unit.
        /// </exception>
        public Task RunAsync(
            DbContext db,
            Func<IUnitOfWork, CancellationToken, Task> operation,
            CancellationToken cancellationToken = default
        )
        {
            return factory.RunAsync(db, operation, IsolationLevel.ReadCommitted, cancellationToken);
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
        /// unit itself is refused once it returns. A context built over the connection of another context that
        /// carries a live unit joins that unit the same way, adopting its transaction.
        /// </summary>
        /// <param name="db">The context to operate on.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token.</param>
        /// <param name="isolation">
        /// Transaction isolation level for a unit this call begins. Ignored when the call joins an already-bound
        /// unit: the block runs at the owner's isolation level.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, and the operation.</param>
        /// <exception cref="InvalidOperationException">
        /// The context's connection is owned by a raw-ADO unit (begin the EF unit first and join it from the ADO
        /// side), the context uses a transaction other than the unit's on that connection, or a joined block
        /// completed, rolled back, or disposed the owner's unit.
        /// </exception>
        public Task RunAsync(
            DbContext db,
            Func<IUnitOfWork, CancellationToken, Task> operation,
            IsolationLevel isolation,
            CancellationToken cancellationToken = default
        )
        {
            return factory.RunAsync(db, operation, isolation, retry: null, cancellationToken);
        }

        /// <summary>
        /// Runs <paramref name="operation" /> as a unit of work inside <paramref name="db" />'s execution strategy,
        /// with the replay and join semantics of the overload that takes an <see cref="IsolationLevel" />, and also
        /// replays the block when <paramref name="retry" /> handles its fault. This is the opt-in for a block that
        /// reads, decides, and saves under an optimistic concurrency token: pass
        /// <see cref="EntityFrameworkConflictRetry.Create" /> and a save that lost the check
        /// (<see cref="DbUpdateConcurrencyException" />) runs the block again on fresh rows.
        /// </summary>
        /// <param name="db">The context to operate on.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token.</param>
        /// <param name="isolation">
        /// Transaction isolation level for a unit this call begins. Ignored when the call joins an already-bound
        /// unit: the block runs at the owner's isolation level.
        /// </param>
        /// <param name="retry">
        /// This call's replay policy, applied on top of the context's execution strategy; <see langword="null" />
        /// replays under the execution strategy alone. No host default applies.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, the operation, and replay delays.</param>
        /// <remarks>
        /// <para>
        /// A replay rolls the attempt back, waits the policy's delay, clears the context's change tracker, and runs
        /// the block again with a fresh transaction and unit, so the block re-reads its rows. Load and edit what the
        /// block changes inside the block, and keep every effect in the unit's transaction (<c>unit.Outbox</c>,
        /// <c>unit.Jobs</c>): anything else the block does repeats on every attempt.
        /// </para>
        /// <para>
        /// The policy never replays a block whose commit started (it may be durable, or in doubt), one that called
        /// <see cref="IUnitOfWork.PreventRetry" />, one that ended its own unit, one that joined a live unit (its
        /// owner decides), one whose context tracked entities before the call, or one in which a second context
        /// that joined the unit over the same connection still tracks an entity (its tracker keeps the failed
        /// attempt's edits). Those faults surface to the caller.
        /// </para>
        /// <para>
        /// The unit learns of a second context only when that context joins it: on its first
        /// <c>RunAsync(second, …)</c>, <c>second.UnitOfWork()</c>, or <c>HeadlessDbContext</c> save in the attempt.
        /// An entity the block adds to or edits on a second context before that is not seen when a replay is
        /// decided, so the block replays, the second context still holds the failed attempt's change, and its
        /// later save applies both attempts' changes. Read and edit through a second context only after it joined
        /// the unit, inside the block, or keep its changes out of a block that can replay.
        /// </para>
        /// <para>
        /// <see cref="RetryStrategyOptions{TResult}.MaxRetryAttempts" /> bounds the replays this policy starts for the
        /// whole call. Under a retrying execution strategy the two budgets add and never multiply: the block runs
        /// at most one time, plus the policy's replays, plus the strategy's retries.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The context's connection is owned by a raw-ADO unit (begin the EF unit first and join it from the ADO
        /// side), the context uses a transaction other than the unit's on that connection, or a joined block
        /// completed, rolled back, or disposed the owner's unit.
        /// </exception>
        public Task RunAsync(
            DbContext db,
            Func<IUnitOfWork, CancellationToken, Task> operation,
            IsolationLevel isolation,
            RetryStrategyOptions? retry,
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
                retry,
                cancellationToken
            );
        }

        /// <summary>
        /// Runs <paramref name="operation" /> as a unit of work inside <paramref name="db" />'s execution
        /// strategy, beginning it at <see cref="IsolationLevel.ReadCommitted" />, and returns its result, with the
        /// same replay semantics as the result-less <c>RunAsync</c> overload.
        /// </summary>
        /// <typeparam name="TResult">Type of the value returned by <paramref name="operation" />.</typeparam>
        /// <param name="db">The context to operate on.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token, returning a result.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, and the operation.</param>
        /// <returns>The result produced by <paramref name="operation" />.</returns>
        /// <exception cref="InvalidOperationException">
        /// The context's connection is owned by a raw-ADO unit (begin the EF unit first and join it from the ADO
        /// side), the context uses a transaction other than the unit's on that connection, or a joined block
        /// completed, rolled back, or disposed the owner's unit.
        /// </exception>
        public Task<TResult> RunAsync<TResult>(
            DbContext db,
            Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default
        )
        {
            return factory.RunAsync(db, operation, IsolationLevel.ReadCommitted, cancellationToken);
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
        /// Transaction isolation level for a unit this call begins. Ignored when the call joins an already-bound
        /// unit: the block runs at the owner's isolation level.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, and the operation.</param>
        /// <exception cref="InvalidOperationException">
        /// The context's connection is owned by a raw-ADO unit (begin the EF unit first and join it from the ADO
        /// side), the context uses a transaction other than the unit's on that connection, or a joined block
        /// completed, rolled back, or disposed the owner's unit.
        /// </exception>
        public Task<TResult> RunAsync<TResult>(
            DbContext db,
            Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
            IsolationLevel isolation,
            CancellationToken cancellationToken = default
        )
        {
            return factory.RunAsync(db, operation, isolation, retry: null, cancellationToken);
        }

        /// <summary>
        /// Runs <paramref name="operation" /> as a unit of work inside <paramref name="db" />'s execution strategy
        /// and returns its result, with the same replay semantics as the result-less
        /// <c>RunAsync(db, operation, isolation, retry, …)</c> overload.
        /// </summary>
        /// <typeparam name="TResult">Type of the value returned by <paramref name="operation" />.</typeparam>
        /// <param name="db">The context to operate on.</param>
        /// <param name="operation">The block receiving the unit and the caller's cancellation token, returning a result.</param>
        /// <param name="isolation">
        /// Transaction isolation level for a unit this call begins. Ignored when the call joins an already-bound
        /// unit: the block runs at the owner's isolation level.
        /// </param>
        /// <param name="retry">
        /// This call's replay policy, applied on top of the context's execution strategy; <see langword="null" />
        /// replays under the execution strategy alone. No host default applies.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to begin, commit, the operation, and replay delays.</param>
        /// <returns>The result produced by the attempt that committed.</returns>
        /// <exception cref="InvalidOperationException">
        /// The context's connection is owned by a raw-ADO unit (begin the EF unit first and join it from the ADO
        /// side), the context uses a transaction other than the unit's on that connection, or a joined block
        /// completed, rolled back, or disposed the owner's unit.
        /// </exception>
        public Task<TResult> RunAsync<TResult>(
            DbContext db,
            Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
            IsolationLevel isolation,
            RetryStrategyOptions? retry,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(operation);

            return _RunCoreAsync(factory, db, operation, isolation, retry, cancellationToken);
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
                    // RunAsync is the join, and a second transaction on the same context is never the answer. A
                    // stale unit is abandoned inline, so its transaction is gone before the begin below.
                    await DbContextUnitOfWorkBinding.ThrowIfBoundAsync(db).ConfigureAwait(false);

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
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        DbContextUnitOfWorkBinding.Bind(db, unit);

        return unit;
    }

    private static Task<TResult> _RunCoreAsync<TResult>(
        IUnitOfWorkFactory factory,
        DbContext db,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        IsolationLevel isolation,
        RetryStrategyOptions? retry,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(db);

        var strategy = db.Database.CreateExecutionStrategy();

        // A call that passed a policy can replay under a strategy that never retries, so the tracker reset
        // follows either source of replay.
        var replayGuard = new DbContextReplayGuard(db, strategy.RetriesOnFailure || retry is not null);
        IUnitOfWorkExecutionStrategy replayLoop = retry is null
            ? new EfUnitOfWorkExecutionStrategy(strategy)
            : new EfConflictReplayExecutionStrategy(strategy, retry);

        return UnitOfWorkRunner.RunAsync(
            async () =>
            {
                // A context (or the connection beneath it) that already carries a live unit joins it: the block
                // runs inline inside the owner's unit, outside any strategy of its own, and a block that ends
                // the unit is refused once it returns.
                if (await DbContextUnitOfWorkBinding.TryGetAsync(db).ConfigureAwait(false) is { } joined)
                {
                    return joined;
                }

                // Refused here, before the strategy, so the caller sees the EF-specific remedy: the connection-owned
                // unit came from a raw-ADO begin, which an EF block can neither own nor join (the context would
                // begin a second transaction on the connection the ADO unit already owns).
                await DbContextUnitOfWorkBinding.ThrowIfConnectionBoundAsync(db).ConfigureAwait(false);

                return null;
            },
            // Retries are legal here: this begin runs inside the strategy, so the retrying check is suppressed and
            // a transient failure replays with a fresh unit and transaction.
            ct =>
            {
                replayGuard.BeforeAttempt();

                return _BeginAsync(factory, db, isolation, rejectRetryingStrategy: false, ct);
            },
            (unitOfWork, ct) => replayGuard.RunAsync(unitOfWork, operation, ct),
            replayLoop,
            UnitOfWorkRunner.LoggerFor(factory),
            cancellationToken
        );
    }
}
