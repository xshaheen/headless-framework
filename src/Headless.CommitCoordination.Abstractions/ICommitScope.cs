// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Owner-side lifecycle handle for a commit coordination scope, returned by <see cref="ICommitScopeFactory.Open" />
/// and by the provider enlistment helpers built on it.
/// </summary>
/// <remarks>
/// The scope is owned by the enlistment caller, not by the infrastructure. The caller must dispose it after the
/// physical transaction completes. Disposing without first calling <see cref="SignalAsync" /> is an implicit
/// rollback: registered work is discarded and scope-local state is disposed. The first terminal claim wins
/// between a signal and a dispose — a dispose that races an already-claimed commit never rolls that commit back.
/// <para>
/// The scope pushes its coordinator onto the ambient stack (<see cref="ICurrentCommitCoordinator" />) when
/// opened and pops it on disposal. The pop is <b>synchronous</b> and happens in the disposal frame — after
/// disposal, <see cref="ICurrentCommitCoordinator.Current" /> returns whatever was ambient before the scope was
/// opened. Disposing a scope while a scope opened inside it is still active throws
/// <see cref="InvalidOperationException" />; disposing a scope whose frame was already unwound (its push was
/// confined to an async flow that has since returned) pops nothing and is otherwise a normal dispose.
/// </para>
/// </remarks>
[PublicAPI]
public interface ICommitScope : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the register-only coordinator visible to consumers that enlist post-commit work.
    /// </summary>
    ICommitCoordinator Coordinator { get; }

    /// <summary>
    /// Signals the terminal outcome of the physical unit of work and, on commit, drains all registered callbacks.
    /// </summary>
    /// <remarks>
    /// There is intentionally no cancellation token: once a terminal outcome is claimed, the drain always runs
    /// to completion so already-committed work is never abandoned. Cancelling the drain would risk discarding
    /// committed durable rows.
    /// <para>
    /// The first call claims the terminal state synchronously, before the returned task is awaited, so a racing
    /// disposal observes the claim. Signals are idempotent per outcome: a repeated signal with the same outcome is
    /// a silent no-op, and a later signal with a conflicting outcome (including a signal after the scope was
    /// disposed) is ignored and logged as a warning. Registered callbacks are invoked in registration order; each
    /// callback fault is captured and the drain continues. If callbacks fault, the task faults after all callbacks
    /// have run — with the single fault, or an <see cref="AggregateException" /> when there are several.
    /// </para>
    /// </remarks>
    /// <param name="outcome">
    /// The terminal outcome: <see cref="CommitOutcome.Committed" /> or <see cref="CommitOutcome.RolledBack" />.
    /// </param>
    /// <returns>A task that completes when the claimed drain (if any) has finished.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="outcome" /> is not <see cref="CommitOutcome.Committed" /> or
    /// <see cref="CommitOutcome.RolledBack" />.
    /// </exception>
    ValueTask SignalAsync(CommitOutcome outcome);
}
