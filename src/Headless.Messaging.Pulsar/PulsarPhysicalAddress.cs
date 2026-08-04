// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Runtime;

namespace Headless.Messaging.Pulsar;

internal static class PulsarPhysicalAddress
{
    public static string Topic(MessageLane lane, string logicalName)
    {
        Argument.IsNotNullOrWhiteSpace(logicalName);

        var separator = logicalName.LastIndexOf('/');
        var prefix = separator >= 0 ? logicalName[..(separator + 1)] : string.Empty;
        var localName = separator >= 0 ? logicalName[(separator + 1)..] : logicalName;

        if (localName.Length == 0)
        {
            throw new InvalidOperationException("Pulsar logical topic must include a local topic name.");
        }

        return $"{prefix}headless-{_Lane(lane)}-{localName}";
    }

    public static string Subscription(MessageLane lane, string groupName) =>
        lane switch
        {
            MessageLane.Bus => $"headless-bus-{TransportNaming.NormalizeDistinct(groupName)}",
            MessageLane.Queue => "headless-queue",
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
