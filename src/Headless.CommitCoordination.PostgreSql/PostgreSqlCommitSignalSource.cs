// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Headless.CommitCoordination.PostgreSql;

/// <summary>
/// Inline PostgreSQL signal source for framework-owned transaction flows.
/// </summary>
[PublicAPI]
public sealed class PostgreSqlCommitSignalSource(CommitScopeFactory scopeFactory) : ICommitSignalSource
{
    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        var capabilities = bindings.Connection is null
            ? []
            : new ICommitCapability[]
            {
                new RelationalCommitContext(() => bindings.Connection, () => bindings.Transaction),
            };

        return scopeFactory.Begin(bindings.Services, capabilities);
    }
}
