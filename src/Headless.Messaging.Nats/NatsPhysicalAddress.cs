// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Runtime;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

internal static class NatsPhysicalAddress
{
    public static string Subject(MessageLane lane, string logicalSubject) => $"headless.{_Lane(lane)}.{logicalSubject}";

    public static string Stream(MessageLane lane, string normalizedLogicalStream) =>
        TransportNaming.NormalizeDistinct($"headless-{_Lane(lane)}-{normalizedLogicalStream}");

    public static string Durable(MessageLane lane, string groupName, string logicalSubject) =>
        TransportNaming.NormalizeDistinct(
            lane switch
            {
                MessageLane.Bus => $"bus-{groupName}-{logicalSubject}",
                MessageLane.Queue => $"queue-{logicalSubject}",
                _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
            }
        );

    public static StreamConfigRetention Retention(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => StreamConfigRetention.Interest,
            MessageLane.Queue => StreamConfigRetention.Workqueue,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };

    private static string _Lane(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => "bus",
            MessageLane.Queue => "queue",
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };
}
