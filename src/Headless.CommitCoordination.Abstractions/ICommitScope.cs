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
    /// <param name="cancellationToken">The cancellation token for the signal operation.</param>
    /// <returns>A task representing signal completion.</returns>
    ValueTask SignalAsync(CommitOutcome outcome, CancellationToken cancellationToken);
}
