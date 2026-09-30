// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Headless.Messaging.InMemory;

/// <summary>
/// Factory for creating in-memory consumer clients.
/// </summary>
internal sealed class InMemoryConsumerClientFactory(MemoryQueue queue) : IConsumerClientFactory
{
    /// <summary>
    /// Creates a new consumer client for the specified subscription.
    /// </summary>
    /// <param name="subscriptionName">The consumer identity on the Bus lane, or the message name on the Queue lane</param>
    /// <param name="concurrency">The concurrency level for the subscription</param>
    /// <returns>A task that returns the created consumer client</returns>
    public Task<IConsumerClient> CreateAsync(
        string subscriptionName,
        byte concurrency,
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = new InMemoryConsumerClient(queue, subscriptionName, concurrency, lane);
        return Task.FromResult<IConsumerClient>(client);
    }
}
