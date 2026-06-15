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
    /// The coordinator committed and no longer accepts work. The terminal outcome is claimed synchronously; the
    /// asynchronous drain of registered work may still be in flight.
    /// </summary>
    Committed,

    /// <summary>
    /// The coordinator rolled back and no longer accepts work.
    /// </summary>
    RolledBack,
}
