using Headless.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemoryQueue;

/// <summary>
/// In-memory message queue implementation for messaging.
/// </summary>
internal sealed class MemoryQueue(ILogger<MemoryQueue> logger)
{
    private static readonly Lock _Lock = new();

    private readonly Dictionary<string, List<string>> _topicGroups = [];
    private readonly Dictionary<string, InMemoryConsumerClient> _consumerClients = [];

    /// <summary>
    /// Registers a consumer client for a specific group.
    /// </summary>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="consumerClient">The consumer client to register</param>
    public void RegisterConsumerClient(string groupId, InMemoryConsumerClient consumerClient)
    {
        lock (_Lock)
        {
            _consumerClients[groupId] = consumerClient;
        }
    }

    /// <summary>
    /// Subscribes a group to specified topics.
    /// </summary>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="topics">The topics to subscribe to</param>
    public void Subscribe(string groupId, IEnumerable<string> topics)
    {
        lock (_Lock)
        {
            foreach (var topic in topics)
            {
                if (_topicGroups.TryGetValue(topic, out var value))
                {
                    if (!value.Contains(groupId, StringComparer.Ordinal))
                    {
                        value.Add(groupId);
                    }
                }
                else
                {
                    _topicGroups.Add(topic, [groupId]);
                }
            }
        }
    }

    /// <summary>
    /// Unsubscribes a consumer group from the queue.
    /// </summary>
    /// <param name="groupId">The consumer group ID</param>
    public void Unsubscribe(string groupId)
    {
        _consumerClients.Remove(groupId);
        logger.LogInformation("Removed consumer client from InMemoryQueue! --> Group: {GroupId}", groupId);
    }

    /// <summary>
    /// Sends a transport message to all subscribed consumer groups.
    /// </summary>
    /// <param name="message">The transport message to send</param>
    /// <exception cref="InvalidOperationException">Thrown when no consumer group has subscribed to the message topic</exception>
    public void Send(TransportMessage message)
    {
        var name = message.GetName();
        lock (_Lock)
        {
            if (_topicGroups.TryGetValue(name, out var groupList))
            {
                foreach (var groupId in groupList)
                {
                    if (_consumerClients.TryGetValue(groupId, out var consumerClient))
                    {
                        var messageCopy = new TransportMessage(
                            message.Headers.ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal),
                            message.Body
                        )
                        {
                            Headers = { [Headers.Group] = groupId },
                        };

                        consumerClient.AddSubscribeMessage(messageCopy);
                    }
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cannot find the corresponding group for {name}. Have you subscribed?"
                );
            }
        }
    }
}
