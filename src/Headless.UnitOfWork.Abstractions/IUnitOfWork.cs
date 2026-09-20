// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Owner-side handle for one unit of work, returned by
/// <see cref="IUnitOfWorkFactory.BeginAsync(UnitOfWorkOptions?, CancellationToken)" /> and
/// <see cref="IUnitOfWorkFactory.Enlist(IUnitOfWorkResource, UnitOfWorkOptions?)" />. Dispose without
/// <see cref="CompleteAsync" /> is an implicit rollback.
/// </summary>
/// <remarks>
/// <see cref="CompleteAsync" /> in owned mode commits the resource's transaction, then drains the
/// <see cref="OnCompleted" /> callbacks in registration order, then disposes scope-local state. Callback faults
/// surface after the drain (one as-is, several as an <see cref="AggregateException" />) and leave the unit
/// <see cref="UnitOfWorkState.Completed" /> — the data is durable; the exception must not be mistaken for a
/// rollback. A commit fault transitions the unit to <see cref="UnitOfWorkState.Failed" /> before the exception
/// propagates, so a second complete throws the "already failed" message rather than re-committing.
/// <para>
/// Disposing without completing rolls the resource back, runs the <see cref="OnFailed" /> callbacks (their
/// faults are logged, never propagated), and disposes scope-local state. An explicit
/// <see cref="RollbackAsync" /> does the same and is idempotent; it is also how the owner of an observed-mode
/// transaction reports its own rollback. A dispose after a terminal state is a no-op.
/// </para>
/// <para>
/// Registrations (<see cref="OnCompleted" />, <see cref="OnFailed" />, <see cref="GetOrAdd{TState}" />) are
/// accepted only while the unit is <see cref="UnitOfWorkState.Active" />; after the terminal state they throw
/// <see cref="InvalidOperationException" />. Every member throws <see cref="ObjectDisposedException" />
/// after the handle is disposed.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>Gets the current lifecycle state of the unit.</summary>
    UnitOfWorkState State { get; }

    /// <summary>
    /// Gets the failure that terminated the unit, or <see langword="null" /> while it is
    /// <see cref="UnitOfWorkState.Active" /> or after a successful completion.
    /// </summary>
    UnitOfWorkFailure? Failure { get; }

    /// <summary>
    /// Gets the enlisted resource, or <see langword="null" /> for a resource-less unit.
    /// </summary>
    /// <remarks>Cast to <see cref="IRelationalUnitOfWorkResource" /> for the live connection and transaction.</remarks>
    IUnitOfWorkResource? Resource { get; }

    /// <summary>
    /// Registers a callback to run after the unit completes successfully (owned mode: after the resource
    /// commits; observed mode: after <see cref="CompleteAsync" /> is called on the already-committed
    /// transaction).
    /// </summary>
    /// <remarks>
    /// Callbacks are process-local: they run once, in registration order, after the outcome is durable, with no
    /// cancellation token, and are not recovered after a crash — durable delivery comes from rows committed
    /// inside the transaction plus the consumer's recovery sweep; a callback is only the fast path. Deregister
    /// by disposing the returned handle while the unit is still active. Registration after a terminal state
    /// throws.
    /// </remarks>
    /// <param name="work">The callback to invoke after the unit completes.</param>
    /// <returns>A handle whose disposal deregisters the callback while the unit is still active.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The unit is no longer active.</exception>
    IDisposable OnCompleted(Func<ValueTask> work);

    /// <summary>
    /// Registers a callback to run after the unit fails: an explicit rollback, an abandon (dispose without
    /// complete), or a commit fault.
    /// </summary>
    /// <remarks>
    /// The justified use case is releasing a non-transactional resource reserved in anticipation of commit. The
    /// callback receives the <see cref="UnitOfWorkFailure" /> carrying the reason. Callback faults are logged
    /// and never propagated — the failure path must not be blocked by its own cleanup. Deregister by disposing
    /// the returned handle while the unit is still active. Registration after a terminal state throws.
    /// </remarks>
    /// <param name="work">The callback to invoke with the failure once the unit fails.</param>
    /// <returns>A handle whose disposal deregisters the callback while the unit is still active.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The unit is no longer active.</exception>
    IDisposable OnFailed(Func<UnitOfWorkFailure, ValueTask> work);

    /// <summary>
    /// Gets or creates a typed, unit-local state object associated with this unit of work, keyed by
    /// <typeparamref name="TState" />.
    /// </summary>
    /// <remarks>
    /// At most one instance of each type exists per unit. The factory runs at most once, under the unit's lock,
    /// so a factory that registers callbacks on construction never registers them twice. State implementing
    /// <see cref="IAsyncDisposable" /> or <see cref="IDisposable" /> is disposed on both terminal outcomes,
    /// after the completion drain (if any).
    /// </remarks>
    /// <typeparam name="TState">The concrete state type.</typeparam>
    /// <param name="factory">A factory that receives this unit and creates the state when absent.</param>
    /// <returns>The existing state if already present, or the newly-created state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The unit is no longer active.</exception>
    TState GetOrAdd<TState>(Func<IUnitOfWork, TState> factory)
        where TState : class;

    /// <summary>
    /// Gets or creates a typed, unit-local state object using a caller-supplied argument to avoid a closure
    /// allocation.
    /// </summary>
    /// <remarks>Functionally identical to <see cref="GetOrAdd{TState}(Func{IUnitOfWork, TState})" />.</remarks>
    /// <typeparam name="TState">The concrete state type.</typeparam>
    /// <typeparam name="TArg">The type of the caller-supplied factory argument.</typeparam>
    /// <param name="arg">The argument forwarded to <paramref name="factory" /> when the state is absent.</param>
    /// <param name="factory">A factory that receives this unit and <paramref name="arg" />, and creates the state when absent.</param>
    /// <returns>The existing state if already present, or the newly-created state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The unit is no longer active.</exception>
    TState GetOrAdd<TState, TArg>(TArg arg, Func<IUnitOfWork, TArg, TState> factory)
        where TState : class;

    /// <summary>
    /// Gets the <see cref="IUnitOfWorkFeature" /> service of type <typeparamref name="TFeature" /> registered in
    /// the host container, or <see langword="null" /> when the host registered none.
    /// </summary>
    /// <remarks>
    /// A lookup, not a registration: the feature is an ordinary singleton service, nothing is created or cached
    /// per unit, and every handle resolves the same instance. The feature receives the handle it is used with as
    /// an argument per call, so it is the call — not this lookup — that answers for the handle's liveness.
    /// </remarks>
    /// <typeparam name="TFeature">The feature's service type; it opts in through <see cref="IUnitOfWorkFeature" />.</typeparam>
    /// <returns>The feature, or <see langword="null" /> when none is registered.</returns>
    /// <exception cref="ObjectDisposedException">This handle was disposed.</exception>
    TFeature? GetFeature<TFeature>()
        where TFeature : class, IUnitOfWorkFeature;

    /// <summary>
    /// Marks this unit as not safely replayable, so an owning execution strategy (for example EF Core's
    /// retrying strategy) must not re-run it after a transient failure.
    /// </summary>
    /// <remarks>Mark before attempting a write whose effects are not retained by the owner's change tracker. The marker never resets.</remarks>
    void PreventRetry();

    /// <summary>Whether <see cref="PreventRetry" /> was called on this unit.</summary>
    bool IsRetryPrevented { get; }

    /// <summary>
    /// Completes the unit: owned mode commits the resource's transaction, then drains
    /// <see cref="OnCompleted" /> registrations, then disposes scope-local state; observed mode drains without
    /// committing.
    /// </summary>
    /// <remarks>
    /// There is no cancellation on the drain: once the outcome is durable the drain runs to completion —
    /// cancelling it would abandon committed work. A callback fault propagates after the drain but leaves the
    /// unit <see cref="UnitOfWorkState.Completed" />. A commit fault transitions the unit to
    /// <see cref="UnitOfWorkState.Failed" /> before the exception propagates.
    /// </remarks>
    /// <param name="cancellationToken">Propagates the caller's cancellation to the resource commit.</param>
    /// <returns>A task that completes when the drain (if any) has finished.</returns>
    /// <exception cref="InvalidOperationException">
    /// The unit already completed or already failed.
    /// </exception>
    ValueTask CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls the unit back explicitly: owned mode rolls the resource's transaction back; observed mode records
    /// the outcome. Runs <see cref="OnFailed" /> callbacks and disposes scope-local state.
    /// </summary>
    /// <remarks>
    /// Idempotent and legal in both modes; a later dispose is a no-op. This is how the owner of an
    /// observed-mode transaction tells the unit its transaction rolled back, and it suppresses the
    /// forgotten-completion warning.
    /// </remarks>
    /// <returns>A task that completes when the failure drain has finished.</returns>
    ValueTask RollbackAsync();
}
