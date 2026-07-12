// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Retry;

/// <summary>
/// Buffers xUnit messages in memory instead of forwarding them immediately. All buffered
/// messages are flushed to the inner bus when <see cref="Dispose"/> is called. Used by
/// <see cref="RetryTestCaseRunner"/> to suppress intermediate failure messages for retried
/// tests — only the final attempt's messages are forwarded.
/// </summary>
/// <param name="innerBus">The real message bus to flush buffered messages to on disposal.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DelayedMessageBus(IMessageBus innerBus) : IMessageBus
{
    private readonly List<IMessageSinkMessage> _messages = [];

    /// <summary>
    /// Buffers <paramref name="message"/> for later forwarding. Always returns
    /// <see langword="true"/> (continue execution) because the inner bus cannot be
    /// consulted without delivering the message.
    /// </summary>
    /// <param name="message">The message to buffer.</param>
    /// <returns><see langword="true"/> always.</returns>
    public bool QueueMessage(IMessageSinkMessage message)
    {
        // Technically speaking, this lock isn't necessary in our case, because we know we're using this
        // message bus for a single test (so there's no possibility of parallelism). However, it's good
        // practice when something might be used where parallel messages might arrive, so it is kept
        // defensively.
        lock (_messages)
        {
            _messages.Add(message);
        }

        // No way to ask the inner bus if they want to cancel without sending them the message, so
        // we just go ahead and continue always.
        return true;
    }

    /// <summary>Flushes all buffered messages to the inner bus in the order they were received.</summary>
    public void Dispose()
    {
        foreach (var message in _messages)
        {
            innerBus.QueueMessage(message);
        }
    }
}
