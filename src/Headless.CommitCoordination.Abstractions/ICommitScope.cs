// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Owner-side lifecycle handle for a commit coordinator.
/// </summary>
[PublicAPI]
public interface ICommitScope : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the register-only coordinator visible to consumers.
    /// </summary>
    ICommitCoordinator Coordinator { get; }

    /// <summary>
    /// Signals the physical unit-of-work outcome.
    /// </summary>
    /// <param name="outcome">The terminal outcome.</param>
    /// <param name="cancellationToken">
    /// Accepted for API symmetry. It does <b>not</b> cancel the drain: once a terminal outcome is claimed, the drain
    /// runs to completion so already-committed work is never abandoned (design decision D9). A cancelled token does
    /// not prevent enlisted callbacks from running.
    /// </param>
    /// <returns>A task representing signal completion.</returns>
    ValueTask SignalAsync(CommitOutcome outcome, CancellationToken cancellationToken);
}
