// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Register-only view of a commit coordination scope for consumers that enlist post-commit work.
/// </summary>
/// <remarks>
/// This interface is <b>register-only</b>: it accepts work registrations but never executes them directly.
/// Execution is driven by the scope owner after the physical unit of work (database transaction) reaches a
/// terminal outcome, through <see cref="ICommitScope.SignalAsync" />.
/// <para>
/// Callbacks registered via <see cref="OnCommit" /> are <b>process-local</b>: the coordinator is an in-memory
/// object, and each callback runs <b>once per coordinator instance</b>, after the physical outcome is durable,
/// on the drain that the commit signal triggers. Callbacks receive no cancellation token: a drain runs to
/// completion once the outcome is durable, because cancelling it would abandon work whose data has already
/// committed. Nothing persists the registrations, so a callback that has not yet run when the process crashes
/// is lost; no relay or sweep recovers it. Durable delivery therefore never comes from a callback — it comes
/// from the row the consumer commits inside the transaction (an outbox or job row) plus that consumer's own
/// recovery sweep. A callback is only the fast path that dispatches such a row sooner.
/// </para>
/// <para>
/// Callbacks drain in registration order. A callback fault does not stop the remaining callbacks: every
/// registered callback runs, and the faults surface to the signaller after the drain (a single fault as-is,
/// several as an <see cref="AggregateException" />). Nothing runs on rollback; rollback only discards the
/// registrations and disposes the scope-local state.
/// </para>
/// <para>
/// Callbacks are savepoint-blind. The coordinator observes the physical transaction's terminal outcome only,
/// so a callback registered inside a savepoint that is later rolled back to still runs when the outer
/// transaction commits. Consumers that register work inside savepoints must tolerate a callback whose
/// originating writes were undone; this trade-off is deliberate and keeps the coordinator free of nested
/// transaction tracking.
/// </para>
/// <para>
/// Every scope is an independent root. Opening a scope while another is ambient does not join it: the new
/// coordinator has its own registrations and its own outcome, and the outer coordinator becomes ambient again
/// once the inner scope is disposed.
/// </para>
/// </remarks>
[PublicAPI]
public interface ICommitCoordinator
{
    /// <summary>
    /// Gets the current lifecycle state of the coordinator.
    /// </summary>
    CommitCoordinatorState State { get; }

    /// <summary>
    /// Gets the live relational connection and transaction the scope was opened with, or <see langword="null" />
    /// when the scope is not bound to a relational transaction (an in-memory or non-relational unit of work).
    /// </summary>
    /// <remarks>
    /// Work that must write durable rows inside the physical transaction (an outbox row, a job row) uses this
    /// handle so the write shares the caller's transaction and disappears with it on rollback.
    /// </remarks>
    IRelationalCommitContext? Relational { get; }

    /// <summary>
    /// Registers a callback to run after the physical unit of work commits.
    /// </summary>
    /// <remarks>
    /// Registration after the coordinator reaches a terminal state throws <see cref="InvalidOperationException" />
    /// — callers that may register after an outcome must check <see cref="State" /> first or catch the
    /// exception. A callback that registers another callback during the drain therefore throws as well: the
    /// state is already terminal when the drain runs. See the type remarks for ordering, fault handling,
    /// cancellation, and durability.
    /// </remarks>
    /// <param name="work">The callback to invoke after the transaction commits.</param>
    /// <returns>
    /// A handle whose disposal deregisters the callback while the coordinator is still active. Once the
    /// coordinator has reached a terminal state, disposal of the handle is a no-op.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="work" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The coordinator is no longer active.</exception>
    IDisposable OnCommit(Func<ValueTask> work);

    /// <summary>
    /// Gets or creates a typed, scope-local state object associated with this coordinator.
    /// </summary>
    /// <remarks>
    /// The state is keyed by <typeparamref name="TState" />: at most one instance of each type exists per
    /// coordinator. The factory is invoked at most once; the result is stored and returned on subsequent calls.
    /// Construction is atomic — concurrent first-access calls serialize, so a factory that enlists a callback
    /// on construction never registers it twice. State that implements <see cref="IAsyncDisposable" /> or
    /// <see cref="IDisposable" /> is disposed after the terminal outcome, on commit and on rollback alike, once
    /// the commit drain (if any) has finished.
    /// </remarks>
    /// <typeparam name="TState">The concrete state type.</typeparam>
    /// <param name="factory">A factory that receives this coordinator and creates the state when absent.</param>
    /// <returns>The existing state if already present, or the newly-created state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The coordinator is no longer active.</exception>
    TState GetOrAdd<TState>(Func<ICommitCoordinator, TState> factory)
        where TState : class;

    /// <summary>
    /// Gets or creates a typed, scope-local state object using a caller-supplied argument to avoid a closure
    /// allocation.
    /// </summary>
    /// <remarks>
    /// Functionally identical to <see cref="GetOrAdd{TState}" /> but the factory receives an extra
    /// <paramref name="arg" /> so callers on hot paths can avoid capturing variables in a closure.
    /// </remarks>
    /// <typeparam name="TState">The concrete state type.</typeparam>
    /// <typeparam name="TArg">The type of the caller-supplied factory argument.</typeparam>
    /// <param name="arg">The argument forwarded to <paramref name="factory" /> when the state is absent.</param>
    /// <param name="factory">A factory that receives this coordinator and <paramref name="arg" />, and creates the state when absent.</param>
    /// <returns>The existing state if already present, or the newly-created state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The coordinator is no longer active.</exception>
    TState GetOrAdd<TState, TArg>(TArg arg, Func<ICommitCoordinator, TArg, TState> factory)
        where TState : class;
}
