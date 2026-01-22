// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;

namespace Headless.Messaging.InMemoryStorage;

internal class InMemoryOutboxTransaction(IDispatcher dispatcher) : OutboxTransaction(dispatcher)
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
