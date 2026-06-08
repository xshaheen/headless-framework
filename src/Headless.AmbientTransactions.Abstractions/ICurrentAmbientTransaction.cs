// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AmbientTransactions;

/// <summary>
/// Stores the current ambient transaction for the async execution context.
/// </summary>
[PublicAPI]
public interface ICurrentAmbientTransaction
{
    IAmbientTransaction? Current { get; set; }
}

/// <summary>
/// Async-local implementation of <see cref="ICurrentAmbientTransaction" />.
/// </summary>
[PublicAPI]
public sealed class AsyncLocalCurrentAmbientTransaction : ICurrentAmbientTransaction
{
    private readonly AsyncLocal<AmbientTransactionHolder> _holder = new() { Value = new AmbientTransactionHolder() };

    public IAmbientTransaction? Current
    {
        get => _holder.Value?.Transaction;
        set
        {
            _holder.Value ??= new AmbientTransactionHolder();
            _holder.Value.Transaction = value;
        }
    }
}

internal sealed class AmbientTransactionHolder
{
    public IAmbientTransaction? Transaction { get; set; }
}
