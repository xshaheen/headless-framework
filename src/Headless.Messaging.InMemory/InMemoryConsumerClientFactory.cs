// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Headless.Messaging.InMemory;

/// <summary>
/// Factory for creating in-memory consumer clients.
/// </summary>
internal sealed class InMemoryConsumerClientFactory(MemoryQueue queue) : IIntentAwareConsumerClientFactory
{
    /// <summary>
    /// Creates a new consumer client for the specified group.
    /// </summary>
    /// <param name="groupName">The consumer group name</param>
    /// <param name="groupConcurrent">The concurrency level for the group</param>
    /// <returns>A task that returns the created consumer client</returns>
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        return CreateAsync(groupName, groupConcurrent, IntentType.Bus);
    }

    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent, IntentType intentType)
    {
        var client = new InMemoryConsumerClient(queue, groupName, groupConcurrent, intentType);
        return Task.FromResult<IConsumerClient>(client);
    }
}
