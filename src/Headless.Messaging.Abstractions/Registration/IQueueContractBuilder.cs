// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

namespace Headless.Messaging.Registration;

/// <summary>Configures the Queue route of one message contract.</summary>
/// <typeparam name="TMessage">The message type the contract describes.</typeparam>
[PublicAPI]
public interface IQueueContractBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Requires a locally supported native affinity mapping for the Queue route at startup.</summary>
    /// <returns>This builder, for chaining.</returns>
    IQueueContractBuilder<TMessage> RequireRoutingAffinity();

    /// <summary>
    /// Pins the delivery mode for autonomous Queue enqueues of this message, overriding the host
    /// <c>MessagingOptions.DefaultDeliveryMode</c>. A per-call <c>QueueOptions.DeliveryMode</c> still
    /// overrides it, and an enlisted enqueue is always durable.
    /// </summary>
    /// <param name="mode">The delivery mode.</param>
    /// <returns>This builder, for chaining.</returns>
    IQueueContractBuilder<TMessage> WithDeliveryMode(DeliveryMode mode);
}
