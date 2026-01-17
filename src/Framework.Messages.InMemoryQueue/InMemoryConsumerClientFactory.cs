using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;

namespace Framework.Messages;

/// <summary>
/// Factory for creating in-memory consumer clients.
/// </summary>
internal sealed class InMemoryConsumerClientFactory(InMemoryQueue queue) : IConsumerClientFactory
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
