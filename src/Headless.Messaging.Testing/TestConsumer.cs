// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.Testing;

/// <summary>
/// A test double for <see cref="IConsume{TMessage}"/> that captures every
/// <see cref="ConsumeContext{TMessage}"/> it receives for assertion.
/// Use <c>harness.WaitForConsumed&lt;T&gt;</c> for awaitable signal-based assertions;
/// use <see cref="ReceivedContexts"/> when you need the full <see cref="ConsumeContext{TMessage}"/>.
/// </summary>
/// <typeparam name="TMessage">The type of message to consume. Must be a reference type.</typeparam>
public sealed class TestConsumer<TMessage> : IConsume<TMessage>
    where TMessage : class
{
    private readonly ConcurrentQueue<ConsumeContext<TMessage>> _receivedContexts = new();

    /// <summary>All captured consume contexts in order received.</summary>
    public IReadOnlyList<ConsumeContext<TMessage>> ReceivedContexts => _receivedContexts.ToArray();

    /// <summary>Projected payloads from <see cref="ReceivedContexts"/>.</summary>
    public IReadOnlyList<TMessage> ReceivedMessages => _receivedContexts.Select(c => c.Message).ToArray();

    /// <summary>The number of messages received so far.</summary>
    public int Count => _receivedContexts.Count;

    /// <summary>Returns <see langword="true"/> if no messages have been received.</summary>
    public bool IsEmpty => _receivedContexts.IsEmpty;

    /// <summary>
    /// Best-effort drain of captured state. Concurrent <see cref="Consume"/> calls
    /// may still enqueue while draining; callers should ensure no publishers are active.
    /// </summary>
    public void Clear()
    {
        while (_receivedContexts.TryDequeue(out _)) { }
    }

    /// <inheritdoc />
    public ValueTask Consume(ConsumeContext<TMessage> context, CancellationToken cancellationToken)
    {
        _receivedContexts.Enqueue(context);
        return ValueTask.CompletedTask;
    }
}
