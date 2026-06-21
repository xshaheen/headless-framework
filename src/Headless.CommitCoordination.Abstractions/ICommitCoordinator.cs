// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Register-only view of a commit coordination scope for consumers that enlist post-commit or post-rollback work.
/// </summary>
/// <remarks>
/// This interface is <b>register-only</b>: it accepts work registrations but never executes them directly.
/// Execution is driven by the infrastructure after the physical unit of work (database transaction) reaches a
/// terminal outcome — commit or rollback — as signalled by the provider's <see cref="ICommitSignalSource" />.
/// <para>
/// Callbacks registered via <see cref="OnCommit" /> or <see cref="OnRollback" /> run at least once after the
/// physical outcome is durable; they are not guaranteed to run exactly once (relay recovery may replay them if
/// an in-process drain is interrupted). Consumers requiring exactly-once semantics must implement idempotency in
/// their callbacks.
/// </para>
/// <para>
/// Child coordinators (opened via <see cref="ICommitScopeFactory.Begin" /> when an ambient scope already exists)
/// promote their registrations to the ambient root: a callback registered on a child fires as part of the root's
/// drain when the root reaches its terminal outcome. A child rollback dooms the root.
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
    /// Registers a callback to run after the physical unit of work commits.
    /// </summary>
    /// <remarks>
    /// Registration is a no-op after the coordinator reaches a terminal state — callers must check
    /// <see cref="State" /> or catch <see cref="InvalidOperationException" /> if calling after an outcome is
    /// possible. Callbacks are invoked in registration order; each receives a <see cref="CommitContext" /> that
    /// carries the service provider and the terminal outcome. The callback receives
    /// <see cref="CancellationToken.None" /> — drains always run to completion to avoid abandoning
    /// already-committed work (design decision D9).
    /// </remarks>
    /// <param name="work">The callback to invoke after the transaction commits.</param>
    /// <returns>
    /// A handle whose disposal deregisters the callback while the coordinator is still active. Once the
    /// coordinator has reached a terminal state, disposal of the handle is a no-op.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="work" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The coordinator is no longer active.</exception>
    IDisposable OnCommit(Func<CommitContext, CancellationToken, ValueTask> work);

    /// <summary>
    /// Registers a callback to run after the physical unit of work rolls back or is abandoned.
    /// </summary>
    /// <remarks>
    /// See <see cref="OnCommit" /> for ordering, cancellation, and terminal-state semantics — they apply
    /// identically to rollback callbacks.
    /// </remarks>
    /// <param name="work">The callback to invoke after the transaction rolls back.</param>
    /// <returns>
    /// A handle whose disposal deregisters the callback while the coordinator is still active. Once the
    /// coordinator has reached a terminal state, disposal of the handle is a no-op.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="work" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The coordinator is no longer active.</exception>
    IDisposable OnRollback(Func<CommitContext, CancellationToken, ValueTask> work);

    /// <summary>
    /// Gets or creates a typed, scope-local work buffer associated with this coordinator.
    /// </summary>
    /// <remarks>
    /// The buffer is keyed by <typeparamref name="TBuffer" />: at most one instance of each buffer type
    /// exists per root coordinator scope. The factory is invoked at most once; the result is stored and
    /// returned on subsequent calls. Buffer construction is atomic — concurrent first-access calls serialize
    /// to avoid double-registration of callbacks the buffer's constructor typically enlists.
    /// </remarks>
    /// <typeparam name="TBuffer">The concrete buffer type, implementing <see cref="ICommitWorkBuffer" />.</typeparam>
    /// <param name="factory">A factory that receives this coordinator and creates the buffer when absent.</param>
    /// <returns>The existing buffer if already present, or the newly-created buffer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The coordinator is no longer active.</exception>
    TBuffer GetOrAdd<TBuffer>(Func<ICommitCoordinator, TBuffer> factory)
        where TBuffer : class, ICommitWorkBuffer;

    /// <summary>
    /// Gets or creates a typed, scope-local work buffer using caller-supplied state to avoid a closure allocation.
    /// </summary>
    /// <remarks>
    /// Functionally identical to <see cref="GetOrAdd{TBuffer}" /> but the
    /// factory receives an extra <paramref name="state" /> argument so callers on hot paths can avoid capturing
    /// variables in a closure.
    /// </remarks>
    /// <typeparam name="TBuffer">The concrete buffer type, implementing <see cref="ICommitWorkBuffer" />.</typeparam>
    /// <typeparam name="TState">The type of the caller-supplied factory state.</typeparam>
    /// <param name="state">The state forwarded to <paramref name="factory" /> when the buffer is absent.</param>
    /// <param name="factory">A factory that receives this coordinator and <paramref name="state" />, and creates the buffer when absent.</param>
    /// <returns>The existing buffer if already present, or the newly-created buffer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The coordinator is no longer active.</exception>
    TBuffer GetOrAdd<TBuffer, TState>(TState state, Func<ICommitCoordinator, TState, TBuffer> factory)
        where TBuffer : class, ICommitWorkBuffer;

    /// <summary>
    /// Attempts to retrieve a provider capability attached when the enclosing scope was opened.
    /// </summary>
    /// <remarks>
    /// Capabilities are attached by the scope owner (typically a provider enlistment helper such as
    /// <c>EnlistCommitCoordination</c>) and are read-only to consumers. For example, a relational provider
    /// attaches an <see cref="IRelationalCommitContext" /> so durable work buffers can reach the live
    /// connection and transaction.
    /// </remarks>
    /// <typeparam name="TCapability">The capability interface to query, deriving from <see cref="ICommitCapability" />.</typeparam>
    /// <param name="capability">
    /// When this method returns <see langword="true" />, contains the attached capability; otherwise
    /// <see langword="null" />.
    /// </param>
    /// <returns><see langword="true" /> when the requested capability is attached; otherwise <see langword="false" />.</returns>
    bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class, ICommitCapability;
}
