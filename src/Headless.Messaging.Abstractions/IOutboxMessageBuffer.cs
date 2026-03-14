// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging;

/// <summary>
/// Buffers stored outbox messages until the ambient transaction is committed.
/// </summary>
/// <remarks>
/// Custom <see cref="IOutboxTransaction" /> implementations that are used with <see cref="IOutboxPublisher" />
/// should also implement this contract so persisted messages can be tracked and flushed on commit.
/// </remarks>
public interface IOutboxMessageBuffer
{
    /// <summary>
    /// Tracks a stored message as part of the current outbox transaction.
    /// </summary>
    /// <param name="message">The stored message.</param>
    void AddToSent(MediumMessage message);
}
