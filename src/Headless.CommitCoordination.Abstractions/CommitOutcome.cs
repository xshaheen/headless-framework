// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Defines the terminal outcome signaled for a commit scope.
/// </summary>
[PublicAPI]
public enum CommitOutcome
{
    /// <summary>
    /// The physical unit of work committed.
    /// </summary>
    Committed,

    /// <summary>
    /// The physical unit of work rolled back or was abandoned.
    /// </summary>
    RolledBack,
}
