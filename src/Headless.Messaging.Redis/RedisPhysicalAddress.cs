// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Redis;

/// <summary>The names the Redis transport derives: stream keys, consumer groups, and wake channels.</summary>
/// <remarks>
/// Stream keys take the host's isolation prefix (<c>Headless:Prefix</c>) in front, joined with <c>:</c>, which a prefix
/// never contains, so two prefixes never produce one key. Consumer groups and wake channels are not prefixed themselves:
/// a group lives inside its stream key and a wake channel is named after it, so the prefixed key already keeps them
/// apart. An empty prefix leaves every name exactly as it is without one.
/// </remarks>
internal static class RedisPhysicalAddress
{
    // Redis consumer group names are binary-safe strings with no documented length limit, so a Bus identity keeps its
    // name unless it holds characters the builder never keeps.
    private static readonly BusNameRules _BusConsumerGroupRules = new(
        maxLength: int.MaxValue,
        isAllowed: static _ => true
    );

    public static string BusStream(string hostPrefix, string logicalName) => _Qualify(hostPrefix, "bus", logicalName);

    public static string QueueStream(string hostPrefix, string logicalName) =>
        _Qualify(hostPrefix, "queue", logicalName);

    public static string ForLane(string hostPrefix, MessageLane lane, string logicalName)
    {
        return lane switch
        {
            MessageLane.Bus => BusStream(hostPrefix, logicalName),
            MessageLane.Queue => QueueStream(hostPrefix, logicalName),
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

    private static string _Qualify(string hostPrefix, string lane, string logicalName)
    {
        Argument.IsNotNull(hostPrefix);
        Argument.IsNotNullOrWhiteSpace(logicalName);

        return hostPrefix.Length == 0
            ? $"headless:messaging:{lane}:{logicalName}"
            : $"{hostPrefix}:headless:messaging:{lane}:{logicalName}";
    }
}
