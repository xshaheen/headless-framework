// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.EntityFramework;

internal sealed class HeadlessCommitCoordinationTransactionCoordinator : IHeadlessTransactionCoordinator
{
    public IHeadlessTransactionScope Enlist(
        DatabaseFacade database,
        IDbContextTransaction transaction,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var scope = database.EnlistCommitCoordination(transaction, services, cancellationToken);
        return new CommitScopeAdapter(scope);
    }

    private sealed class CommitScopeAdapter(ICommitScope scope) : IHeadlessTransactionScope
    {
        private readonly CommitRetryGuard _retryGuard = scope.Coordinator.GetOrAdd(static _ => new CommitRetryGuard());

        public bool IsRetryPrevented => _retryGuard.IsRetryPrevented;

        public void Dispose()
        {
            scope.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return scope.DisposeAsync();
        }
    }
}
