// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// The terminal outcome signalled for a commit coordination scope.
/// </summary>
/// <remarks>
/// The default value is <see cref="Unspecified" /> and cannot be passed to a commit coordination signal.
/// </remarks>
[PublicAPI]
public enum CommitOutcome
{
    /// <summary>
    /// No terminal outcome has been selected. Passing this value to a commit coordination signal is invalid.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The physical unit of work (database transaction) committed successfully. Registered
    /// <see cref="ICommitCoordinator.OnCommit" /> callbacks are drained.
    /// </summary>
    Committed = 1,

    /// <summary>
    /// The physical unit of work rolled back or was abandoned (e.g. an exception was thrown or the scope was
    /// disposed without signalling). Registered <see cref="ICommitCoordinator.OnRollback" /> callbacks are
    /// drained.
    /// </summary>
    RolledBack = 2,
}
