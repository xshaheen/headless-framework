// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;

namespace Headless.CommitCoordination.InMemory;

/// <summary>
/// Driven in-memory signal source for owners that explicitly signal commit or rollback.
/// </summary>
[PublicAPI]
public sealed class InMemoryCommitSignalSource(ICommitScopeFactory scopeFactory) : ICommitSignalSource
{
    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        return scopeFactory.Begin(bindings.Services, bindings.Capabilities);
    }
}
