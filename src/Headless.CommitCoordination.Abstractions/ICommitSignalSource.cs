// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Attaches provider-specific commit or rollback detection to a coordinator scope.
/// </summary>
[PublicAPI]
public interface ICommitSignalSource
{
    /// <summary>
    /// Attaches a coordinator to the provider signal source.
    /// </summary>
    /// <param name="bindings">The coordinator bindings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The attached scope.</returns>
    ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken);
}
