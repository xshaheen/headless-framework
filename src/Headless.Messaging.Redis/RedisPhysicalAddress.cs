// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Redis;

internal static class RedisPhysicalAddress
{
    // Redis consumer group names are binary-safe strings with no documented length limit, so a Bus identity keeps its
    // name unless it holds characters the builder never keeps.
    private static readonly BusNameRules _BusConsumerGroupRules = new(
        maxLength: int.MaxValue,
        isAllowed: static _ => true
    );

    public static string BusStream(string logicalName) => _Qualify("bus", logicalName);

    public static string QueueStream(string logicalName) => _Qualify("queue", logicalName);

    public static string ForLane(MessageLane lane, string logicalName)
    {
        return lane switch
        {
            MessageLane.Bus => BusStream(logicalName),
            MessageLane.Queue => QueueStream(logicalName),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };
    }

    /// <summary>
    /// Returns the consumer group a consumer reads each stream through: its consumer identity on the Bus lane, and its
    /// subscription name on the Queue lane.
    /// </summary>
    public static string ConsumerGroup(MessageLane lane, string subscriptionName)
    {
        return lane switch
        {
            MessageLane.Bus => BusNameBuilder.Build(subscriptionName, _BusConsumerGroupRules),
            MessageLane.Queue => Argument.IsNotNullOrWhiteSpace(subscriptionName),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };
    }

    /// <summary>
    /// Returns the pub/sub channel a publisher announces new entries of <paramref name="stream"/> on. Channels and keys
    /// are separate namespaces, and distinct streams map to distinct channels.
    /// </summary>
    public static string WakeChannel(string stream) => $"{Argument.IsNotNullOrWhiteSpace(stream)}:wake";

    private static string _Qualify(string lane, string logicalName)
    {
        Argument.IsNotNullOrWhiteSpace(logicalName);
        return $"headless:messaging:{lane}:{logicalName}";
    }
}
