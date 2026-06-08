// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Commit signal source used by EF Core integration points.
/// </summary>
[PublicAPI]
public sealed class EntityFrameworkCommitSignalSource(CommitScopeFactory scopeFactory) : ICommitSignalSource
{
    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        var capabilities = bindings.Connection is null
            ? []
            : new ICommitCapability[] { new RelationalCommitContext(() => bindings.Connection, () => null) };

        return scopeFactory.Begin(bindings.Services, capabilities);
    }
}
