// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Describes the lifecycle state of a commit coordinator.
/// </summary>
[PublicAPI]
public enum CommitCoordinatorState
{
    /// <summary>
    /// The coordinator accepts new work registrations.
    /// </summary>
    Active,

    /// <summary>
    /// The coordinator is draining a terminal outcome.
    /// </summary>
    Draining,

    /// <summary>
    /// The coordinator committed and no longer accepts work.
    /// </summary>
    Committed,

    /// <summary>
    /// The coordinator rolled back and no longer accepts work.
    /// </summary>
    RolledBack,
}
