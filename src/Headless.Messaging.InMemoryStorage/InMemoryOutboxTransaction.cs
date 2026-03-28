// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;

namespace Headless.Messaging.InMemoryStorage;

internal sealed class InMemoryOutboxTransaction(IDispatcher dispatcher, IOutboxTransactionAccessor accessor)
    : OutboxTransaction(dispatcher, accessor)
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
