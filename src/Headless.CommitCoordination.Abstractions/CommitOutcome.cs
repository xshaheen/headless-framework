// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// The terminal outcome signalled for a commit coordination scope.
/// </summary>
[PublicAPI]
public enum CommitOutcome
{
    /// <summary>
    /// The physical unit of work (database transaction) committed successfully. Registered
    /// <see cref="ICommitCoordinator.OnCommit" /> callbacks are drained.
    /// </summary>
    Committed,

    /// <summary>
    /// The physical unit of work rolled back or was abandoned (e.g. an exception was thrown or the scope was
    /// disposed without signalling). Registered <see cref="ICommitCoordinator.OnRollback" /> callbacks are
    /// drained.
    /// </summary>
    RolledBack,
}
