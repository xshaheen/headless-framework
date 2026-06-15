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
    /// Signals the physical unit-of-work outcome. There is intentionally no cancellation token: once a terminal
    /// outcome is claimed the drain runs to completion so already-committed work is never abandoned (design
    /// decision D9). Cancellation would risk discarding committed work, so the signal does not accept a token.
    /// </summary>
    /// <param name="outcome">The terminal outcome.</param>
    /// <returns>A task representing signal completion.</returns>
    ValueTask SignalAsync(CommitOutcome outcome);
}
