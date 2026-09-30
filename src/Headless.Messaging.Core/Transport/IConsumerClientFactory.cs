// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Factory interface for creating instances of <see cref="IConsumerClient"/>.
/// </summary>
public interface IConsumerClientFactory
{
    /// <summary>
    /// Asynchronously creates a new <see cref="IConsumerClient"/> instance.
    /// </summary>
    /// <param name="subscriptionName">
    /// What the client subscribes as: the consumer identity on the <see cref="MessageLane.Bus"/> lane, where one
    /// client binds every message the identity consumes, or the message name on the <see cref="MessageLane.Queue"/>
    /// lane, which has one consumer per message.
    /// </param>
    /// <param name="concurrency">The maximum number of concurrent messages to consume.</param>
    /// <param name="lane">The explicit semantic lane the client consumes.</param>
    /// <param name="cancellationToken">Token to cancel consumer creation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the created <see cref="IConsumerClient"/> instance.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    Task<IConsumerClient> CreateAsync(
        string subscriptionName,
        byte concurrency,
        MessageLane lane,
        CancellationToken cancellationToken = default
    );
}
