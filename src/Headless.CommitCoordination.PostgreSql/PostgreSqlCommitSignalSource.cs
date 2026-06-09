// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.CommitCoordination;
using Headless.Checks;

namespace Headless.CommitCoordination.PostgreSql;

/// <summary>
/// Inline PostgreSQL signal source for framework-owned transaction flows.
/// </summary>
/// <remarks>
/// Unlike SQL Server, Npgsql exposes no diagnostic listener that surfaces the native transaction commit/rollback
/// edge, so there is no out-of-band detector. The model is therefore <b>inline (caller-driven)</b>: the caller
/// enlists a scope keyed by the open <c>NpgsqlTransaction</c>, then drives the signal by calling
/// <see cref="SignalCommittedAsync(object, CancellationToken)" /> or
/// <see cref="SignalRolledBackAsync(object, CancellationToken)" /> immediately after committing or rolling that
/// transaction back. If the caller never signals, disposing the returned scope discards the enlisted work.
/// </remarks>
[PublicAPI]
public sealed class PostgreSqlCommitSignalSource(CommitScopeFactory scopeFactory) : ICommitSignalSource
{
    private readonly ConcurrentDictionary<object, ICommitScope> _scopes = new();

    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(bindings);
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
    /// <remarks>An absent key is silently ignored — the normal case for an uncoordinated transaction.</remarks>
    /// <param name="providerTransactionKey">The provider transaction key (the open <c>NpgsqlTransaction</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signal task.</returns>
    public async ValueTask SignalCommittedAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(providerTransactionKey);

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
    /// <param name="providerTransactionKey">The provider transaction key (the open <c>NpgsqlTransaction</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signal task.</returns>
    public async ValueTask SignalRolledBackAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(providerTransactionKey);

        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            return;
        }

        await using var ownedScope = scope;
        await ownedScope.SignalAsync(CommitOutcome.RolledBack, cancellationToken).ConfigureAwait(false);
    }
}
