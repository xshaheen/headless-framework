// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

namespace Headless.Messaging.Registration;

/// <summary>Configures the Bus route of one message contract.</summary>
/// <typeparam name="TMessage">The message type the contract describes.</typeparam>
[PublicAPI]
public interface IBusContractBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Requires a locally supported native affinity mapping for the Bus route at startup.</summary>
    /// <returns>This builder, for chaining.</returns>
    IBusContractBuilder<TMessage> RequireRoutingAffinity();

    /// <summary>
    /// Pins the delivery mode for autonomous Bus publishes of this message, overriding the host
    /// <c>MessagingOptions.DefaultDeliveryMode</c>. A per-call <c>PublishOptions.DeliveryMode</c> still
    /// overrides it, and an enlisted publish is always durable.
    /// </summary>
    /// <param name="mode">The delivery mode.</param>
    /// <returns>This builder, for chaining.</returns>
    IBusContractBuilder<TMessage> WithDeliveryMode(DeliveryMode mode);
}
