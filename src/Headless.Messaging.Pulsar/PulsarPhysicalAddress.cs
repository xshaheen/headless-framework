// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Pulsar;

internal static class PulsarPhysicalAddress
{
    private const string _BusSubscriptionPrefix = "headless-bus-";

    // A broker with strictlyVerifySubscriptionName accepts only [-=:.\w] in a new subscription name. Pulsar documents no
    // length limit; 255 keeps the cursor name, which the broker stores URL-encoded, within common key and path limits.
    private static readonly BusNameRules _BusSubscriptionRules = new(
        maxLength: 255 - _BusSubscriptionPrefix.Length,
        isAllowed: static c => c is '-' or '=' or ':' or '.' or '_'
    );

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

    /// <summary>
    /// Returns the subscription a consumer reads through: one per consumer identity on the Bus lane, and one shared
    /// subscription on each Queue topic.
    /// </summary>
    public static string Subscription(MessageLane lane, string subscriptionName) =>
        lane switch
        {
            MessageLane.Bus => _BusSubscriptionPrefix + BusNameBuilder.Build(subscriptionName, _BusSubscriptionRules),
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
