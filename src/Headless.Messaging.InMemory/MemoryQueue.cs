// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemory;

/// <summary>
/// In-memory message queue implementation for messaging.
/// </summary>
internal sealed class MemoryQueue(ILogger<MemoryQueue> logger)
{
    private readonly Lock _lock = new();

    private readonly Dictionary<(IntentType IntentType, string Topic), List<string>> _topicGroups = [];
    private readonly Dictionary<(IntentType IntentType, string GroupId), List<InMemoryConsumerClient>> _consumerClients =
        [];
    private readonly Dictionary<(IntentType IntentType, string GroupId), int> _nextClientIndexes = [];
    private readonly Dictionary<string, int> _nextQueueGroupIndexes = [];

    /// <summary>
    /// Registers a consumer client for a specific group.
    /// </summary>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="consumerClient">The consumer client to register</param>
    public void RegisterConsumerClient(IntentType intentType, string groupId, InMemoryConsumerClient consumerClient)
    {
        lock (_lock)
        {
            var key = (intentType, groupId);
            if (!_consumerClients.TryGetValue(key, out var clients))
            {
                clients = [];
                _consumerClients[key] = clients;
            }

            clients.Add(consumerClient);
        }
    }

    /// <summary>
    /// Subscribes a group to specified topics.
    /// </summary>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="topics">The topics to subscribe to</param>
    public void Subscribe(IntentType intentType, string groupId, IEnumerable<string> topics)
    {
        lock (_lock)
        {
            foreach (var topic in topics)
            {
                var key = (intentType, topic);
                if (_topicGroups.TryGetValue(key, out var value))
                {
                    if (!value.Contains(groupId, StringComparer.Ordinal))
                    {
                        value.Add(groupId);
                    }
                }
                else
                {
                    _topicGroups.Add(key, [groupId]);
                }
            }
        }
    }

    /// <summary>
    /// Unsubscribes a consumer group from the queue.
    /// </summary>
    /// <param name="groupId">The consumer group ID</param>
    public void Unsubscribe(IntentType intentType, string groupId, InMemoryConsumerClient consumerClient)
    {
        lock (_lock)
        {
            var key = (intentType, groupId);
            if (_consumerClients.TryGetValue(key, out var clients))
            {
                clients.Remove(consumerClient);
                if (clients.Count == 0)
                {
                    _consumerClients.Remove(key);
                    _nextClientIndexes.Remove(key);
                    _RemoveGroupSubscriptions(intentType, groupId);
                }
            }
        }

        logger.ConsumerRemoved(groupId);
    }

    private void _RemoveGroupSubscriptions(IntentType intentType, string groupId)
    {
        foreach (var (key, groups) in _topicGroups.ToArray())
        {
            if (key.IntentType != intentType)
            {
                continue;
            }

            groups.RemoveAll(group => string.Equals(group, groupId, StringComparison.Ordinal));

            if (groups.Count == 0)
            {
                _topicGroups.Remove(key);
                _nextQueueGroupIndexes.Remove(key.Topic);
            }
        }
    }

    /// <summary>
    /// Drains all pending messages from every registered consumer client.
    /// </summary>
    public void DrainAllPendingMessages()
    {
        List<InMemoryConsumerClient> snapshot;
        lock (_lock)
        {
            snapshot = [.. _consumerClients.Values.SelectMany(static clients => clients)];
        }

        foreach (var client in snapshot)
        {
            client.DrainPendingMessages();
        }
    }

    /// <summary>
    /// Sends a transport message to all subscribed bus consumer groups.
    /// </summary>
    /// <param name="message">The transport message to send</param>
    /// <exception cref="InvalidOperationException">Thrown when no consumer group has subscribed to the message topic</exception>
    public void SendBus(TransportMessage message)
    {
        var name = message.GetName();
        lock (_lock)
        {
            if (_topicGroups.TryGetValue((IntentType.Bus, name), out var groupList))
            {
                foreach (var groupId in groupList)
                {
                    _TryDeliverToGroup(IntentType.Bus, groupId, message);
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

    /// <summary>
    /// Sends a transport message to one subscribed queue consumer group.
    /// </summary>
    /// <param name="message">The transport message to send</param>
    /// <exception cref="InvalidOperationException">Thrown when no consumer group has subscribed to the message topic</exception>
    public void SendQueue(TransportMessage message)
    {
        var name = message.GetName();
        lock (_lock)
        {
            if (!_topicGroups.TryGetValue((IntentType.Queue, name), out var groupList) || groupList.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot find the corresponding group for {name}. Have you subscribed?"
                );
            }

            var startIndex = _nextQueueGroupIndexes.TryGetValue(name, out var index) ? index : 0;
            for (var offset = 0; offset < groupList.Count; offset++)
            {
                var currentIndex = (startIndex + offset) % groupList.Count;
                var groupId = groupList[currentIndex];

                if (_TryDeliverToGroup(IntentType.Queue, groupId, message))
                {
                    _nextQueueGroupIndexes[name] = (currentIndex + 1) % groupList.Count;
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Cannot find an active consumer client for {name}. Have you subscribed?"
            );
        }
    }

    private bool _TryDeliverToGroup(IntentType intentType, string groupId, TransportMessage message)
    {
        var key = (intentType, groupId);
        if (!_consumerClients.TryGetValue(key, out var clients) || clients.Count == 0)
        {
            return false;
        }

        var nextIndex = _nextClientIndexes.TryGetValue(key, out var index) ? index : 0;
        var consumerClient = clients[nextIndex % clients.Count];
        _nextClientIndexes[key] = (nextIndex + 1) % clients.Count;

        var messageCopy = new TransportMessage(
            message.Headers.ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal),
            message.Body
        )
        {
            Headers = { [Headers.Group] = groupId },
        };

        consumerClient.AddSubscribeMessage(messageCopy);
        return true;
    }
}

internal static partial class MemoryQueueLog
{
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Removed consumer client from InMemory! --> Group: {GroupId}"
    )]
    public static partial void ConsumerRemoved(this ILogger logger, string groupId);
}
