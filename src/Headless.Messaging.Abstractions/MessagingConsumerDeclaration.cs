// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Reliability;

namespace Headless.Messaging;

/// <summary>One message handled by one attribute-declared consumer, as a generated module declared it.</summary>
/// <param name="Source">The module that declared it, for conflict messages.</param>
/// <param name="ConsumerType">The consumer class.</param>
/// <param name="MessageType">The consumed message type.</param>
/// <param name="Lane">The lane the consumer's attribute names.</param>
/// <param name="Identity">The consumer identity.</param>
/// <param name="EveryInstance">Whether every process receives every message; always false on the Queue lane.</param>
/// <param name="Dispatch">The generated dispatch that runs the consumer class.</param>
/// <param name="OnSubscriptionEstablished">The generated subscription hook of the class, when it has one.</param>
/// <param name="FailurePolicyFactory">Creates the failure policy the attribute declares, when it declares one.</param>
/// <param name="ResponseType">
/// The response type of a responder, or <see langword="null"/> for a consumer that does not answer requests.
/// </param>
internal sealed record MessagingConsumerDeclaration(
    string Source,
    Type ConsumerType,
    Type MessageType,
    MessageLane Lane,
    string Identity,
    bool EveryInstance,
    MessageConsumerDispatch Dispatch,
    SubscriptionEstablishedDispatch? OnSubscriptionEstablished,
    Func<FailurePolicy>? FailurePolicyFactory = null,
    Type? ResponseType = null
);
