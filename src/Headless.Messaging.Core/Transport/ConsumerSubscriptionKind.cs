// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

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
