// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transactions;

public interface IOutboxTransactionAccessor
{
    IOutboxTransaction? Current { get; set; }
}

internal sealed class AsyncLocalOutboxTransactionAccessor : IOutboxTransactionAccessor
{
    private readonly AsyncLocal<OutboxTransactionHolder> _holder = new();

    public IOutboxTransaction? Current
    {
        get => _holder.Value?.Transaction;
        set
        {
            _holder.Value ??= new OutboxTransactionHolder();
            _holder.Value.Transaction = value;
        }
    }
}
