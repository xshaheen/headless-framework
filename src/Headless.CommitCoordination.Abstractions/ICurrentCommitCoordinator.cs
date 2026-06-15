// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Provides read-only ambient access to the current commit coordinator.
/// </summary>
[PublicAPI]
public interface ICurrentCommitCoordinator
{
    /// <summary>
    /// Gets the current coordinator, if a scope is active.
    /// </summary>
    ICommitCoordinator? Current { get; }
}
