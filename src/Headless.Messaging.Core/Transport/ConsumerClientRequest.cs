// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>What the messaging core asks a transport to create one consumer client for.</summary>
/// <remarks>
/// A transport maps a <see cref="ConsumerSubscriptionKind.Competing"/> request to the durable subscription that
/// <see cref="SubscriptionName"/> names, shared by every process. It maps a
/// <see cref="ConsumerSubscriptionKind.EveryInstance"/> request to a subscription that belongs to this process alone,
/// derived from <see cref="SubscriptionName"/> and <see cref="InstanceId"/>, and that the broker removes once the process
/// no longer holds it.
/// </remarks>
[PublicAPI]
public sealed record ConsumerClientRequest
{
    /// <summary>Initializes a new instance of the <see cref="ConsumerClientRequest"/> class.</summary>
    /// <param name="subscriptionName">
    /// What the client subscribes as: the consumer identity on the <see cref="MessageLane.Bus"/> lane, where one client
    /// binds every message the identity consumes, or the message name on the <see cref="MessageLane.Queue"/> lane.
    /// </param>
    /// <param name="concurrency">The maximum number of messages the client hands to the core at once.</param>
    /// <param name="lane">The semantic lane the client consumes.</param>
    /// <param name="kind">How the processes that open this subscription share its messages.</param>
    /// <param name="instanceId">
    /// The <see cref="MessagingInstanceId"/> of the requesting process. Required for
    /// <see cref="ConsumerSubscriptionKind.EveryInstance"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="subscriptionName"/> is empty, <paramref name="lane"/> or <paramref name="kind"/> is undefined, or an
    /// every-instance request names the Queue lane or an empty <paramref name="instanceId"/>.
    /// </exception>
    public ConsumerClientRequest(
        string subscriptionName,
        byte concurrency,
        MessageLane lane,
        ConsumerSubscriptionKind kind = ConsumerSubscriptionKind.Competing,
        Guid instanceId = default
    )
    {
        SubscriptionName = Argument.IsNotNullOrWhiteSpace(subscriptionName);
        Concurrency = concurrency;
        Lane = Argument.IsInEnum(lane);
        Kind = Argument.IsInEnum(kind);
        InstanceId = instanceId;

        if (kind is ConsumerSubscriptionKind.EveryInstance)
        {
            if (lane is not MessageLane.Bus)
            {
                throw new ArgumentException(
                    "Only the Bus lane has every-instance subscriptions; the Queue lane is point-to-point.",
                    nameof(kind)
                );
            }

            Argument.IsNotEmpty(instanceId);
        }
    }

    /// <summary>The consumer identity on the Bus lane, or the message name on the Queue lane.</summary>
    public string SubscriptionName { get; }

    /// <summary>The maximum number of messages the client hands to the core at once.</summary>
    public byte Concurrency { get; }

    /// <summary>The semantic lane the client consumes.</summary>
    public MessageLane Lane { get; }

    /// <summary>How the processes that open this subscription share its messages.</summary>
    public ConsumerSubscriptionKind Kind { get; }

    /// <summary>
    /// The <see cref="MessagingInstanceId"/> of the requesting process, or <see cref="Guid.Empty"/> when a caller outside
    /// the messaging core creates a competing client.
    /// </summary>
    public Guid InstanceId { get; }
}

/// <summary>How the processes that open one subscription share its messages.</summary>
[PublicAPI]
public enum ConsumerSubscriptionKind
{
    /// <summary>
    /// Every process that opens the subscription competes for its messages, so each message reaches one of them. The
    /// subscription is durable: messages published while no process consumes it wait for the next one.
    /// </summary>
    Competing = 0,

    /// <summary>
    /// Every process receives every message through a subscription of its own, which exists only while the process
    /// holds it. Delivery is at most once and has no backlog, and the Bus lane is the only lane with this kind.
    /// </summary>
    EveryInstance = 1,
}
