// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Tests.Fixtures;

namespace Tests.Helpers;

/// <summary>Test subscriber that collects received messages for assertions.</summary>
[PublicAPI]
public sealed class TestSubscriber : IConsume<TestMessage>
{
    private readonly ConcurrentQueue<ConsumeContext<TestMessage>> _receivedContexts = new();
    private readonly Lock _lock = new();

    // Signaled on every Consume(), then replaced with a fresh TCS so subsequent waiters
    // can await the next arrival. Always read/written under _lock.
    private TaskCompletionSource<bool> _arrivedTcs = new();

    /// <summary>Gets the received message contexts.</summary>
    public IReadOnlyCollection<ConsumeContext<TestMessage>> ReceivedContexts => _receivedContexts;

    /// <summary>Gets the received messages.</summary>
    public IReadOnlyCollection<TestMessage> ReceivedMessages => _receivedContexts.Select(c => c.Message).ToList();

    public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _receivedContexts.Enqueue(context);
            _arrivedTcs.TrySetResult(true);
            _arrivedTcs = new TaskCompletionSource<bool>();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Waits for at least one message to be received, or timeout.</summary>
    public async Task<bool> WaitForMessageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            Task waitTask;

            lock (_lock)
            {
                if (!_receivedContexts.IsEmpty)
                {
                    return true;
                }

                waitTask = _arrivedTcs.Task;
            }

            await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Waits until <paramref name="count"/> messages have been received, or timeout.</summary>
    public async Task<bool> WaitForCountAsync(
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                Task waitTask;

                lock (_lock)
                {
                    if (_receivedContexts.Count >= count)
                    {
                        return true;
                    }

                    waitTask = _arrivedTcs.Task;
                }

                await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Clears all received messages and resets the wait state.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _receivedContexts.Clear();
            _arrivedTcs = new TaskCompletionSource<bool>();
        }
    }
}
