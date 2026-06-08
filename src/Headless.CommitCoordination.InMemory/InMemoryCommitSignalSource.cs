// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Headless.CommitCoordination.InMemory;

/// <summary>
/// Driven in-memory signal source for owners that explicitly signal commit or rollback.
/// </summary>
[PublicAPI]
public sealed class InMemoryCommitSignalSource(CommitScopeFactory scopeFactory) : ICommitSignalSource
{
    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        return scopeFactory.Begin(bindings.Services);
    }
}
