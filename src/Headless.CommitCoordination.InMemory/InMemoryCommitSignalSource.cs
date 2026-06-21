// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.CommitCoordination.InMemory;

/// <summary>
/// Caller-driven in-memory signal source for flows where the owner explicitly signals the commit or rollback
/// outcome, rather than relying on a database diagnostic or interceptor.
/// </summary>
/// <remarks>
/// This source is primarily used in tests and non-relational scenarios. The caller holds the
/// <see cref="ICommitScope" /> returned by <see cref="Attach" />, performs its work, then calls
/// <see cref="ICommitScope.SignalAsync" /> to drain the registered callbacks. Disposing the scope without
/// signalling discards the enlisted work (treated as a rollback).
/// </remarks>
[PublicAPI]
public sealed class InMemoryCommitSignalSource(ICommitScopeFactory scopeFactory) : ICommitSignalSource
{
    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        // Own a child DI scope (matching the relational sources): a background drain offloaded by a sync
        // un-signalled Dispose resolves its callbacks from a scope that outlives the caller's request scope,
        // which may already be disposed by the time that drain runs. Disposed by TrackedCommitScope after the
        // drain or on un-signalled dispose.
        var ownedServices = bindings.Services.CreateAsyncScope();

        try
        {
            var scope = scopeFactory.Begin(ownedServices.ServiceProvider, bindings.Capabilities);

            return new TrackedCommitScope(scope, static _ => { }, ownedServices);
        }
        catch
        {
            ownedServices.Dispose();
            throw;
        }
    }
}
