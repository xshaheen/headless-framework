// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.CommitCoordination;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Correlates SQL Server diagnostic commit signals to commit coordination scopes, keyed by the connection's
/// <c>ClientConnectionId</c>. The out-of-band <see cref="SqlServerCommitDiagnosticObserver" /> turns the native
/// SqlClient commit/rollback edge into a signal here.
/// </summary>
[PublicAPI]
public sealed class SqlServerCommitSignalSource(CommitScopeFactory scopeFactory) : ICommitSignalSource
{
    private readonly ConcurrentDictionary<object, ICommitScope> _scopes = new();

    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = scopeFactory.Begin(bindings.Services, bindings.Capabilities);

        if (bindings.ProviderTransactionKey is not null)
        {
            _scopes[bindings.ProviderTransactionKey] = scope;
        }

        return scope;
    }

    /// <summary>
    /// Signals a commit for a previously attached provider transaction key.
    /// </summary>
    /// <remarks>
    /// The diagnostic observer fires for every SqlClient transaction edge, most of which are not coordinated; an
    /// absent key is the normal case and is silently ignored (never a warning).
    /// </remarks>
    /// <param name="providerTransactionKey">The provider transaction key (the connection's <c>ClientConnectionId</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signal task.</returns>
    public async ValueTask SignalCommittedAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerTransactionKey);

        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            return;
        }

        await using var ownedScope = scope;
        await ownedScope.SignalAsync(CommitOutcome.Committed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Signals a rollback for a previously attached provider transaction key.
    /// </summary>
    /// <param name="providerTransactionKey">The provider transaction key (the connection's <c>ClientConnectionId</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signal task.</returns>
    public async ValueTask SignalRolledBackAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerTransactionKey);

        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            return;
        }

        await using var ownedScope = scope;
        await ownedScope.SignalAsync(CommitOutcome.RolledBack, cancellationToken).ConfigureAwait(false);
    }
}
