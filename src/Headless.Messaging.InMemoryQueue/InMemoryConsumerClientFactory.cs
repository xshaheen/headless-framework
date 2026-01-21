using Headless.Messaging.Transport;

namespace Headless.Messaging.InMemoryQueue;

/// <summary>
/// Factory for creating in-memory consumer clients.
/// </summary>
internal sealed class InMemoryConsumerClientFactory(MemoryQueue queue) : IConsumerClientFactory
{
    /// <summary>
    /// Creates a new consumer client for the specified group.
    /// </summary>
    /// <param name="groupName">The consumer group name</param>
    /// <param name="groupConcurrent">The concurrency level for the group</param>
    /// <returns>A task that returns the created consumer client</returns>
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        var client = new InMemoryConsumerClient(queue, groupName, groupConcurrent);
        return Task.FromResult<IConsumerClient>(client);
    }
}
