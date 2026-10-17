// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// The broker objects a host's registered consumers and messages need, as a transport's
/// <see cref="ITransportTopologyProvisioner"/> sees them: built from the consumers and messages registered before the
/// host was built, never from subscriptions an <c>IRuntimeSubscriber</c> adds while it runs.
/// </summary>
/// <param name="Subscriptions">Every consumer subscription this host starts, competing and every-instance.</param>
/// <param name="Messages">Every registered message with its lane, consumed on this host or only published.</param>
[PublicAPI]
public sealed record MessagingTopology(
    IReadOnlyList<TopologySubscription> Subscriptions,
    IReadOnlyList<TopologyMessage> Messages
)
{
    /// <summary>A topology with no subscription and no message: what a provisioner needs for the shared objects alone.</summary>
    public static MessagingTopology Empty { get; } = new([], []);
}

/// <summary>One consumer subscription: what a consumer client is created for.</summary>
/// <param name="SubscriptionName">
/// The consumer identity on the <see cref="MessageLane.Bus"/> lane, or the message name on the
/// <see cref="MessageLane.Queue"/> lane, as <see cref="ConsumerClientRequest.SubscriptionName"/> carries it.
/// </param>
/// <param name="Lane">The lane the subscription consumes.</param>
/// <param name="Kind">
/// How processes share it. A provisioner skips <see cref="ConsumerSubscriptionKind.EveryInstance"/>: its objects belong
/// to one process, which creates them when it subscribes.
/// </param>
/// <param name="MessageNames">The message names the subscription binds, ordered ordinally.</param>
[PublicAPI]
public sealed record TopologySubscription(
    string SubscriptionName,
    MessageLane Lane,
    ConsumerSubscriptionKind Kind,
    IReadOnlyList<string> MessageNames
);

/// <summary>One registered message on one lane.</summary>
/// <param name="MessageName">The message name the transport routes by.</param>
/// <param name="Lane">The lane it is published on.</param>
[PublicAPI]
public sealed record TopologyMessage(string MessageName, MessageLane Lane);
