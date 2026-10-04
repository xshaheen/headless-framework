// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

internal static class NatsPhysicalAddress
{
    // nats-server rejects durable names longer than JSMaxNameLen (255 bytes) and names containing whitespace, '.', '*',
    // '>', '/', or '\' (isValidAssetName): they become subject tokens and file names.
    private static readonly BusNameRules _BusDurableRules = new(
        maxLength: 255,
        isAllowed: static c => c is not ('.' or '*' or '>' or '/' or '\\')
    );

    public static string Subject(MessageLane lane, string logicalSubject) => $"headless.{_Lane(lane)}.{logicalSubject}";

    public static string Stream(MessageLane lane, string normalizedLogicalStream) =>
        TransportNaming.NormalizeDistinct($"headless-{_Lane(lane)}-{normalizedLogicalStream}");

    /// <summary>
    /// Returns the durable consumer for one subject: one per consumer identity and subject on the Bus lane, and one per
    /// subject on the Queue lane.
    /// </summary>
    public static string Durable(MessageLane lane, string subscriptionName, string logicalSubject) =>
        lane switch
        {
            MessageLane.Bus => BusNameBuilder.Build($"bus-{subscriptionName}-{logicalSubject}", _BusDurableRules),
            MessageLane.Queue => TransportNaming.NormalizeDistinct($"queue-{logicalSubject}"),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };

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
