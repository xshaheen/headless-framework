// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.CommitCoordination;
using Microsoft.Extensions.Logging;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Correlates SQL Server diagnostic commit signals to commit coordination scopes.
/// </summary>
[PublicAPI]
public sealed partial class SqlServerCommitSignalSource(
    CommitScopeFactory scopeFactory,
    ILogger<SqlServerCommitSignalSource> logger
) : ICommitSignalSource
{
    private readonly ConcurrentDictionary<object, ICommitScope> _scopes = new();

    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        var capabilities = bindings.Connection is null
            ? []
            : new ICommitCapability[] { new RelationalCommitContext(() => bindings.Connection, () => null) };

        var scope = scopeFactory.Begin(bindings.Services, capabilities);

        if (bindings.ProviderTransactionKey is not null)
        {
            _scopes[bindings.ProviderTransactionKey] = scope;
        }

        return scope;
    }

    /// <summary>
    /// Signals a commit for a previously attached provider transaction key.
    /// </summary>
    /// <param name="providerTransactionKey">The provider transaction key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signal task.</returns>
    public async ValueTask SignalCommittedAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerTransactionKey);

        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            LogMissingScope(logger, providerTransactionKey);
            return;
        }

        await using var ownedScope = scope;
        await ownedScope.SignalAsync(CommitOutcome.Committed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Signals a rollback for a previously attached provider transaction key.
    /// </summary>
    /// <param name="providerTransactionKey">The provider transaction key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signal task.</returns>
    public async ValueTask SignalRolledBackAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerTransactionKey);

        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            LogMissingScope(logger, providerTransactionKey);
            return;
        }

        await using var ownedScope = scope;
        await ownedScope.SignalAsync(CommitOutcome.RolledBack, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "No commit coordination scope is attached for SQL Server transaction key {ProviderTransactionKey}."
    )]
    private static partial void LogMissingScope(ILogger logger, object providerTransactionKey);
}
