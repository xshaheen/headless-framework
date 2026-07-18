// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.EntityFramework.Contexts.Runtime;

internal interface IHeadlessTransactionCoordinator
{
    IHeadlessTransactionScope Enlist(
        DatabaseFacade database,
        IDbContextTransaction transaction,
        IServiceProvider services,
        CancellationToken cancellationToken
    );
}

internal interface IHeadlessTransactionScope : IDisposable, IAsyncDisposable;

internal sealed class NullHeadlessTransactionCoordinator : IHeadlessTransactionCoordinator
{
    public static readonly NullHeadlessTransactionCoordinator Instance = new();

    private NullHeadlessTransactionCoordinator() { }

    public IHeadlessTransactionScope Enlist(
        DatabaseFacade database,
        IDbContextTransaction transaction,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return NullHeadlessTransactionScope.Instance;
    }

    private sealed class NullHeadlessTransactionScope : IHeadlessTransactionScope
    {
        public static readonly NullHeadlessTransactionScope Instance = new();

        private NullHeadlessTransactionScope() { }

        public void Dispose() { }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
