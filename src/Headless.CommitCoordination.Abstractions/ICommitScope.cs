// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Owner-side lifecycle handle for a commit coordination scope, returned by provider enlistment helpers and
/// <see cref="ICommitSignalSource.Attach" />.
/// </summary>
/// <remarks>
/// The scope is owned by the enlistment caller, not by the infrastructure. The caller must dispose it after the
/// physical transaction completes. Disposing without first calling <see cref="SignalAsync" /> is treated as an
/// implicit rollback: any registered work is discarded.
/// <para>
/// For a <b>child</b> scope — one opened via <see cref="ICommitScopeFactory.Begin" /> while an ambient coordinator
/// already exists — that implicit rollback is <b>not local</b>: it dooms the entire <b>root</b> transaction,
/// draining the root's rollback callbacks and causing any later root commit signal to be an ignored no-op. This is
/// the fail-closed default (an abandoned nested unit rolls the whole transaction back). Signal a child explicitly
/// before disposing it unless you intend to roll the whole transaction back.
/// </para>
/// <para>
/// The scope pushes the enclosing coordinator onto the ambient stack (<see cref="ICurrentCommitCoordinator" />)
/// at creation and pops it on disposal. The pop is <b>synchronous</b> and happens in the disposal frame —
/// after disposal, <see cref="ICurrentCommitCoordinator.Current" /> no longer returns this scope's coordinator.
/// </para>
/// </remarks>
[PublicAPI]
public interface ICommitScope : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the register-only coordinator visible to consumers that enlist post-commit or post-rollback work.
    /// </summary>
    ICommitCoordinator Coordinator { get; }

    /// <summary>
    /// Signals the terminal outcome of the physical unit of work and drains all registered callbacks.
    /// </summary>
    /// <remarks>
    /// There is intentionally no cancellation token: once a terminal outcome is claimed, the drain always runs
    /// to completion so already-committed work is never abandoned (design decision D9). Cancelling the drain
    /// would risk discarding committed durable rows.
    /// <para>
    /// The first call claims the terminal state synchronously; subsequent calls (e.g. a racing disposal) observe
    /// the already-claimed state and return a completed task. Registered callbacks are invoked in registration
    /// order; each callback fault is captured and the drain continues. If one or more callbacks fault, the
    /// signal task faults with an <see cref="AggregateException" /> after all callbacks have run.
    /// </para>
    /// </remarks>
    /// <param name="outcome">The terminal outcome: <see cref="CommitOutcome.Committed" /> or <see cref="CommitOutcome.RolledBack" />.</param>
    /// <returns>A task that completes when all registered callbacks have been drained.</returns>
    ValueTask SignalAsync(CommitOutcome outcome);
}
