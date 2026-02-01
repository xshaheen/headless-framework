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
    private TaskCompletionSource<bool> _messageReceivedTcs = new();

    /// <summary>Gets the received message contexts.</summary>
    public IReadOnlyCollection<ConsumeContext<TestMessage>> ReceivedContexts => _receivedContexts;

    /// <summary>Gets the received messages.</summary>
    public IReadOnlyCollection<TestMessage> ReceivedMessages => _receivedContexts.Select(c => c.Message).ToList();

    /// <summary>Gets a task that completes when at least one message is received.</summary>
    public Task MessageReceivedTask => _messageReceivedTcs.Task;

    public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
    {
        _receivedContexts.Enqueue(context);
        _messageReceivedTcs.TrySetResult(true);
        return ValueTask.CompletedTask;
    }

    /// <summary>Waits for a message to be received with timeout.</summary>
    /// <param name="timeout">Max wait time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a message was received within the timeout.</returns>
    public async Task<bool> WaitForMessageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await _messageReceivedTcs.Task.WaitAsync(cts.Token).AnyContext();
            return true;
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
            _messageReceivedTcs = new TaskCompletionSource<bool>();
        }
    }
}
