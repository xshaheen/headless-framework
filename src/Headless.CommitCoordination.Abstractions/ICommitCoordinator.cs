// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Registers work that should run after the physical unit of work reaches a terminal outcome.
/// </summary>
[PublicAPI]
public interface ICommitCoordinator
{
    /// <summary>
    /// Gets the coordinator state.
    /// </summary>
    CommitCoordinatorState State { get; }

    /// <summary>
    /// Registers work that runs after the unit of work commits.
    /// </summary>
    /// <param name="work">The work to invoke after commit.</param>
    /// <returns>A handle that deregisters the work while the coordinator is still active.</returns>
    IDisposable OnCommit(Func<CommitContext, CancellationToken, ValueTask> work);

    /// <summary>
    /// Registers work that runs after the unit of work rolls back.
    /// </summary>
    /// <param name="work">The work to invoke after rollback.</param>
    /// <returns>A handle that deregisters the work while the coordinator is still active.</returns>
    IDisposable OnRollback(Func<CommitContext, CancellationToken, ValueTask> work);

    /// <summary>
    /// Gets or creates a typed, scope-local work buffer.
    /// </summary>
    /// <typeparam name="TBuffer">The buffer type.</typeparam>
    /// <param name="factory">Factory used to create the buffer when it is absent.</param>
    /// <returns>The existing or newly-created buffer.</returns>
    TBuffer GetOrAdd<TBuffer>(Func<ICommitCoordinator, TBuffer> factory)
        where TBuffer : class, ICommitWorkBuffer;

    /// <summary>
    /// Gets or creates a typed, scope-local work buffer using caller-supplied factory state.
    /// </summary>
    /// <typeparam name="TBuffer">The buffer type.</typeparam>
    /// <typeparam name="TState">The factory state type.</typeparam>
    /// <param name="state">The state passed to <paramref name="factory" /> when the buffer is absent.</param>
    /// <param name="factory">Factory used to create the buffer when it is absent.</param>
    /// <returns>The existing or newly-created buffer.</returns>
    TBuffer GetOrAdd<TBuffer, TState>(TState state, Func<ICommitCoordinator, TState, TBuffer> factory)
        where TBuffer : class, ICommitWorkBuffer;

    /// <summary>
    /// Attempts to get a provider capability attached by the scope owner.
    /// </summary>
    /// <typeparam name="TCapability">The capability type.</typeparam>
    /// <param name="capability">The capability when available.</param>
    /// <returns><see langword="true" /> when the capability exists.</returns>
    bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class, ICommitCapability;
}
