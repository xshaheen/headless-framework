// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.CommitCoordination;
using Headless.Checks;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Correlates EF Core <c>IDbTransactionInterceptor</c> commit/rollback edges to commit coordination scopes.
/// </summary>
[PublicAPI]
public sealed class EntityFrameworkCommitSignalSource(CommitScopeFactory scopeFactory) : ICommitSignalSource
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
    /// Signals a commit for the scope correlated to the given transaction, if one is attached.
    /// </summary>
    /// <remarks>
    /// The interceptor fires for every EF transaction, most of which are not coordinated; an absent key is the
    /// normal case and is silently ignored (never a warning).
    /// </remarks>
    /// <param name="providerTransactionKey">The transaction correlation key (the intercepted <c>DbTransaction</c>).</param>
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
    /// Signals a rollback for the scope correlated to the given transaction, if one is attached.
    /// </summary>
    /// <param name="providerTransactionKey">The transaction correlation key (the intercepted <c>DbTransaction</c>).</param>
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
