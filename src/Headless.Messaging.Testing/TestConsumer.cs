// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.Testing;

/// <summary>
/// A test double for <see cref="IConsume{TMessage}"/> that captures every
/// <see cref="ConsumeContext{TMessage}"/> it receives for assertion.
/// </summary>
/// <typeparam name="TMessage">The type of message to consume. Must be a reference type.</typeparam>
public sealed class TestConsumer<TMessage> : IConsume<TMessage>
    where TMessage : class
{
    private readonly ConcurrentQueue<ConsumeContext<TMessage>> _receivedContexts = new();
    private readonly Lock _lock = new();

    /// <summary>All captured consume contexts in order received.</summary>
    public IReadOnlyList<ConsumeContext<TMessage>> ReceivedContexts => _receivedContexts.ToArray();

    /// <summary>Projected payloads from <see cref="ReceivedContexts"/>.</summary>
    public IReadOnlyList<TMessage> ReceivedMessages => _receivedContexts.Select(c => c.Message).ToArray();

    /// <summary>Resets captured state. Thread-safe.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            while (_receivedContexts.TryDequeue(out _)) { }
        }
    }

    /// <inheritdoc />
    public ValueTask Consume(ConsumeContext<TMessage> context, CancellationToken cancellationToken)
    {
        _receivedContexts.Enqueue(context);
        return ValueTask.CompletedTask;
    }
}
