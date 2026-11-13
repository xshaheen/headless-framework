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
    /// Returns a Bus consumer identity's durable. A durable name is scoped to its stream, so the identity has one durable
    /// of this name on each stream it reads, filtering on every subject it consumes there: one pull loop per stream
    /// keeps the stream's order across message types.
    /// </summary>
    /// <remarks>
    /// The underscore keeps these names apart from the <see cref="LegacyBusDurable"/> names, which all start with
    /// <c>bus-</c>. With a dash, identity <c>a-b</c> would get the name identity <c>a</c> gave its durable for message
    /// <c>b</c>, and the first bind of <c>a</c> would take that durable over and delete it.
    /// </remarks>
    public static string BusDurable(string subscriptionName) =>
        BusNameBuilder.Build($"bus_{subscriptionName}", _BusDurableRules);

    /// <summary>
    /// Returns the per-subject Bus durable that releases before the per-stream <see cref="BusDurable"/> created. A
    /// consumer reads where these stopped when it first creates its per-stream durable, then deletes them.
    /// </summary>
    public static string LegacyBusDurable(string subscriptionName, string logicalSubject) =>
        BusNameBuilder.Build($"bus-{subscriptionName}-{logicalSubject}", _BusDurableRules);

    /// <summary>
    /// Returns the Queue durable for one subject. Every host that consumes the message shares it, and hosts consume
    /// different messages of one stream, so a Queue durable stays per subject.
    /// </summary>
    public static string QueueDurable(string logicalSubject) =>
        TransportNaming.NormalizeDistinct($"queue-{logicalSubject}");

    // Bus streams use interest retention: a Bus durable starts at DeliverPolicy.New, so a message published before a
    // consumer exists never reaches it, and keeping it under limits retention would only cost storage.
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
