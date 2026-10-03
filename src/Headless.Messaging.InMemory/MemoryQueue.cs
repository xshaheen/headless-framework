// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemory;

/// <summary>
/// In-memory message queue implementation for messaging.
/// </summary>
internal sealed class MemoryQueue(ILogger<MemoryQueue> logger)
{
    private readonly Lock _lock = new();

    private readonly Dictionary<(MessageLane Lane, string MessageName), List<string>> _messageNameGroups = [];
    private readonly Dictionary<(MessageLane Lane, string GroupId), List<InMemoryConsumerClient>> _consumerClients = [];
    private readonly Dictionary<(MessageLane Lane, string GroupId), int> _nextClientIndexes = [];
    private readonly Dictionary<string, int> _nextQueueGroupIndexes = [];

    // Reply addresses live here rather than on a transport instance, so hosts that share one MemoryQueue also share
    // reply addresses, as processes sharing one broker do.
    private readonly Dictionary<string, InMemoryReplyListener> _replyListeners = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a consumer client for a specific group.
    /// </summary>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="consumerClient">The consumer client to register</param>
    public void RegisterConsumerClient(MessageLane lane, string groupId, InMemoryConsumerClient consumerClient)
    {
        lock (_lock)
        {
            var key = (lane, groupId);
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
    public void Subscribe(MessageLane lane, string groupId, IEnumerable<string> messageNames)
    {
        lock (_lock)
        {
            foreach (var messageName in messageNames)
            {
                var key = (lane, messageName);
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
    public void Unsubscribe(MessageLane lane, string groupId, InMemoryConsumerClient consumerClient)
    {
        lock (_lock)
        {
            var key = (lane, groupId);
            if (_consumerClients.TryGetValue(key, out var clients))
            {
                clients.Remove(consumerClient);
                if (clients.Count == 0)
                {
                    _consumerClients.Remove(key);
                    _nextClientIndexes.Remove(key);
                    _RemoveGroupSubscriptions(lane, groupId);
                }
            }
        }

        logger.ConsumerRemoved(groupId);
    }

    private void _RemoveGroupSubscriptions(MessageLane lane, string groupId)
    {
        foreach (var (key, groups) in _messageNameGroups.ToArray())
        {
            if (key.Lane != lane)
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
        var name = message.Name;
        lock (_lock)
        {
            if (!_messageNameGroups.TryGetValue((MessageLane.Bus, name), out var groupList))
            {
                logger.NoSubscribersBus(name);
                return;
            }

            foreach (var groupId in groupList)
            {
                _TryDeliverToGroup(MessageLane.Bus, groupId, message);
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
        var name = message.Name;
        lock (_lock)
        {
            if (!_messageNameGroups.TryGetValue((MessageLane.Queue, name), out var groupList) || groupList.Count == 0)
            {
                logger.NoSubscribersQueue(name);
                return;
            }

            var startIndex = _nextQueueGroupIndexes.TryGetValue(name, out var index) ? index : 0;
            for (var offset = 0; offset < groupList.Count; offset++)
            {
                var currentIndex = (startIndex + offset) % groupList.Count;
                var groupId = groupList[currentIndex];

                if (_TryDeliverToGroup(MessageLane.Queue, groupId, message))
                {
                    _nextQueueGroupIndexes[name] = (currentIndex + 1) % groupList.Count;
                    return;
                }
            }

            // All groups have no active clients — drop silently (matches real-broker semantics).
            logger.NoActiveConsumerQueue(name);
        }
    }

    /// <summary>Makes <paramref name="listener"/> the receiver of replies sent to <paramref name="address"/>.</summary>
    public void RegisterReplyListener(string address, InMemoryReplyListener listener)
    {
        lock (_lock)
        {
            _replyListeners.Add(address, listener);
        }
    }

    /// <summary>Stops delivering replies sent to <paramref name="address"/> to <paramref name="listener"/>.</summary>
    public void UnregisterReplyListener(string address, InMemoryReplyListener listener)
    {
        lock (_lock)
        {
            if (_replyListeners.TryGetValue(address, out var registered) && ReferenceEquals(registered, listener))
            {
                _replyListeners.Remove(address);
            }
        }
    }

    /// <summary>Whether a reply listener is registered at <paramref name="address"/>.</summary>
    internal bool HasReplyListener(string address)
    {
        lock (_lock)
        {
            return _replyListeners.ContainsKey(address);
        }
    }

    /// <summary>
    /// Hands a copy of <paramref name="reply"/> to the listener at <paramref name="address"/>. A reply to an address
    /// nobody listens on is dropped, as the caller has gone.
    /// </summary>
    public void SendReply(string address, TransportMessage reply)
    {
        InMemoryReplyListener? listener;
        lock (_lock)
        {
            _replyListeners.TryGetValue(address, out listener);
        }

        if (listener is null)
        {
            logger.NoReplyListener(address);
            return;
        }

        listener.Deliver(_Copy(reply));
    }

    private bool _TryDeliverToGroup(MessageLane lane, string groupId, TransportMessage message)
    {
        var key = (lane, groupId);
        if (!_consumerClients.TryGetValue(key, out var clients) || clients.Count == 0)
        {
            return false;
        }

        var nextIndex = _nextClientIndexes.TryGetValue(key, out var index) ? index : 0;
        var consumerClient = clients[nextIndex % clients.Count];
        _nextClientIndexes[key] = (nextIndex + 1) % clients.Count;

        consumerClient.AddSubscribeMessage(_Copy(message));
        return true;
    }

    // Each receiver gets its own headers, as from a real broker, so one consumer's changes never reach another.
    private static TransportMessage _Copy(TransportMessage message)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(message.Headers, StringComparer.Ordinal),
            message.Body
        );
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

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Debug,
        Message = "No reply listener at address '{ReplyAddress}'. Reply dropped (no-op)."
    )]
    public static partial void NoReplyListener(this ILogger logger, string replyAddress);
}
