// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.CommitCoordination;

/// <summary>
/// Shared attach logic for keyed commit signal sources (EF Core, SQL Server, PostgreSQL). Each provider's signal
/// source differs only in its duplicate-key wording and its own scope dictionary; the enlist mechanics — own a
/// child DI scope, begin an independent coordinator root, wrap it in a self-evicting <see cref="TrackedCommitScope" />, and
/// reject a duplicate key — are identical. Internal: provider packages call this via <c>InternalsVisibleTo</c>.
/// </summary>
internal static class CommitSignalSourceAttach
{
    public static ICommitScope Attach(
        ICommitScopeFactory scopeFactory,
        CommitCoordinatorBindings bindings,
        ConcurrentDictionary<object, ICommitScope> scopes,
        Func<object, Exception> duplicateFault,
        CancellationToken cancellationToken
    )
    {
        // MA0045: this method is synchronous by design (see ICommitSignalSource.Attach) — it pushes the ambient
        // coordinator onto an AsyncLocal<T> stack in the caller's frame, so making it async would strand that push
        // in a separate execution context and break ICurrentCommitCoordinator.Current. The error-path Dispose()
        // calls below therefore stay synchronous.
#pragma warning disable MA0045
        Argument.IsNotNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        // Own a child DI scope so a background (un-signalled) drain resolves callbacks from services that outlive
        // the caller's request scope. Disposed by TrackedCommitScope after the drain or on un-signalled dispose.
        var ownedServices = bindings.Services.CreateAsyncScope();
        ICommitScope scope;

        try
        {
            // A signal-source attachment represents one physical transaction. If another transaction is
            // already ambient, joining it would discard these capabilities and bind durable work to the
            // outer connection/transaction. Keep physical transactions as independent coordinator roots;
            // logical child scopes still use ICommitScopeFactory.Begin directly.
            scope = scopeFactory.BeginNew(ownedServices.ServiceProvider, bindings.Capabilities);
        }
        catch
        {
            ownedServices.Dispose();
            throw;
        }

        if (bindings.ProviderTransactionKey is not { } key)
        {
            return new TrackedCommitScope(scope, static _ => { }, ownedServices);
        }

        // Remove-if-equal: a tracked scope only ever evicts its OWN entry, so a successor transaction reusing the
        // same key (after this one's drain removed it) is never evicted by this scope's disposal.
        var trackedScope = new TrackedCommitScope(
            scope,
            self => scopes.TryRemove(new KeyValuePair<object, ICommitScope>(key, self)),
            ownedServices
        );

        if (scopes.TryAdd(key, trackedScope))
        {
            return trackedScope;
        }

        trackedScope.Dispose();

        // The provider both logs the duplicate and builds the exception, keeping the log+throw decision in one
        // place per provider (its own [LoggerMessage] + message wording).
        throw duplicateFault(key);
#pragma warning restore MA0045
    }
}
