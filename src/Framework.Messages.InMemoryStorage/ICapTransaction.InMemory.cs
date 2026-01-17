// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Transactions;
using Framework.Messages.Transport;

// ReSharper disable once CheckNamespace

namespace DotNetCore.CAP.InMemoryStorage;

internal class InMemoryOutboxTransaction(IDispatcher dispatcher) : OutboxTransactionBase(dispatcher)
{
    public override void Commit()
    {
        Flush();
    }

    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public override void Rollback()
    {
        //Ignore
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
