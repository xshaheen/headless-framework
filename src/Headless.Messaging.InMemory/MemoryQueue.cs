// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemory;

/// <summary>
/// In-memory message queue implementation for messaging.
/// </summary>
internal sealed class MemoryQueue(ILogger<MemoryQueue> logger)
{
    private readonly Lock _lock = new();

    private readonly Dictionary<(IntentType IntentType, string MessageName), List<string>> _messageNameGroups = [];
    private readonly Dictionary<
        (IntentType IntentType, string GroupId),
        List<InMemoryConsumerClient>
    > _consumerClients = [];
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
    /// Subscribes a group to specified message names.
    /// </summary>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="messageNames">The message names to subscribe to</param>
    public void Subscribe(IntentType intentType, string groupId, IEnumerable<string> messageNames)
    {
        lock (_lock)
        {
            foreach (var messageName in messageNames)
            {
                var key = (intentType, messageName);
                if (_messageNameGroups.TryGetValue(key, out var value))
                {
                    if (!value.Contains(groupId, StringComparer.Ordinal))
                    {
                        value.Add(groupId);
                    }
                }
                else
                {
                    _messageNameGroups.Add(key, [groupId]);
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
        foreach (var (key, groups) in _messageNameGroups.ToArray())
        {
            if (key.IntentType != intentType)
            {
                continue;
            }

            groups.RemoveAll(group => string.Equals(group, groupId, StringComparison.Ordinal));

            if (groups.Count == 0)
            {
                _messageNameGroups.Remove(key);
                _nextQueueGroupIndexes.Remove(key.MessageName);
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
    /// When no subscriber is registered for the message name the message is silently dropped (no-op),
    /// matching real-broker semantics (Kafka, RabbitMQ, Redis all treat publish-without-subscriber as a no-op).
    /// </summary>
    /// <param name="message">The transport message to send</param>
    public void SendBus(TransportMessage message)
    {
        var name = message.GetName();
        lock (_lock)
        {
            if (!_messageNameGroups.TryGetValue((IntentType.Bus, name), out var groupList))
            {
                logger.NoSubscribersBus(name);
                return;
            }

            foreach (var groupId in groupList)
            {
                _TryDeliverToGroup(IntentType.Bus, groupId, message);
            }
        }
    }

    /// <summary>
    /// Sends a transport message to one subscribed queue consumer group.
    /// When no subscriber is registered for the message name the message is silently dropped (no-op),
    /// matching real-broker semantics (Kafka, RabbitMQ, Redis all treat publish-without-subscriber as a no-op).
    /// </summary>
    /// <param name="message">The transport message to send</param>
    public void SendQueue(TransportMessage message)
    {
        var name = message.GetName();
        lock (_lock)
        {
            if (!_messageNameGroups.TryGetValue((IntentType.Queue, name), out var groupList) || groupList.Count == 0)
            {
                logger.NoSubscribersQueue(name);
                return;
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

            // All groups have no active clients — drop silently (matches real-broker semantics).
            logger.NoActiveConsumerQueue(name);
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

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Warning,
        Message = "No bus subscriber registered for message name '{MessageName}'. Message dropped (no-op)."
    )]
    public static partial void NoSubscribersBus(this ILogger logger, string messageName);

    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Warning,
        Message = "No queue subscriber registered for message name '{MessageName}'. Message dropped (no-op)."
    )]
    public static partial void NoSubscribersQueue(this ILogger logger, string messageName);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Warning,
        Message = "No active consumer client for queue message name '{MessageName}'. Message dropped (no-op)."
    )]
    public static partial void NoActiveConsumerQueue(this ILogger logger, string messageName);
}
