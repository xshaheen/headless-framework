// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Redis;

internal static class RedisPhysicalAddress
{
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

    private static string _Qualify(string lane, string logicalName)
    {
        Argument.IsNotNullOrWhiteSpace(logicalName);
        return $"headless:messaging:{lane}:{logicalName}";
    }
}
