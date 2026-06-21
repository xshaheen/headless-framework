// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Describes the lifecycle state of a commit coordinator.
/// </summary>
/// <remarks>
/// State transitions are one-way and atomic: <see cref="Active" /> → <see cref="Committed" /> or
/// <see cref="Active" /> → <see cref="RolledBack" />. Once a terminal state is reached, no further
/// transitions occur. Attempts to register new work (via <see cref="ICommitCoordinator.OnCommit" />,
/// <see cref="ICommitCoordinator.OnRollback" />, or <see cref="ICommitCoordinator.GetOrAdd{TBuffer}" />)
/// after the coordinator leaves <see cref="Active" /> throw <see cref="InvalidOperationException" />.
/// </remarks>
[PublicAPI]
public enum CommitCoordinatorState
{
    /// <summary>
    /// The coordinator is open and accepts new work registrations.
    /// </summary>
    Active,

    /// <summary>
    /// The physical unit of work committed. The coordinator no longer accepts work registrations. The terminal
    /// outcome is claimed synchronously on the commit edge; the asynchronous drain of registered callbacks may
    /// still be in flight when this state is first observed.
    /// </summary>
    Committed,

    /// <summary>
    /// The physical unit of work rolled back or was abandoned. The coordinator no longer accepts work
    /// registrations. Any registered rollback callbacks are drained asynchronously after the state is set.
    /// </summary>
    RolledBack,
}
