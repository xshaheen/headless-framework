// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Registration;

/// <summary>
/// One message route recorded in the service collection and drained when messaging starts: either the lane half of a
/// <c>Message&lt;T&gt;(name, version)</c> contract, which declares the route's settings, or one generated consumer,
/// which joins the route of its message.
/// </summary>
internal sealed record MessageRegistration(
    Type MessageType,
    MessageLane Lane,
    string? MessageName,
    Func<object, string?>? CorrelationSelector,
    IReadOnlyDictionary<Type, object> ProviderConfigs,
    IReadOnlyList<MessageConsumerRegistration> Consumers,
    string ContractVersion = MessageOptions.InitialContractVersion,
    bool RequiresRoutingAffinity = false,
    // Only a contract pins a policy; a consumer registration leaves it null so a publish falls through to the host
    // default.
    DeliveryMode? DeliveryMode = null,
    // False for consumer registrations. They carry no message-level settings, so they join the contract that declares
    // the message.
    bool DeclaresMessage = true
)
{
    /// <summary>
    /// A registration that contributes one consumer and declares no message-level settings, so it can join the
    /// contract that does declare the message.
    /// </summary>
    internal static MessageRegistration ConsumerOnly(
        Type messageType,
        MessageLane lane,
        string? messageName,
        MessageConsumerRegistration consumer,
        string contractVersion
    ) =>
        new(
            messageType,
            lane,
            messageName,
            CorrelationSelector: null,
            ProviderConfigs: new Dictionary<Type, object>(),
            Consumers: [consumer],
            ContractVersion: contractVersion,
            DeclaresMessage: false
        );
}

/// <summary>One attribute-declared consumer of one message, as its generated module declared it.</summary>
/// <param name="ConsumerType">The consumer class.</param>
/// <param name="Lane">The lane its attribute names.</param>
/// <param name="ConsumerIdentity">The consumer identity from its attribute.</param>
/// <param name="Dispatch">The generated dispatch that runs the consumer class.</param>
internal sealed record MessageConsumerRegistration(
    Type ConsumerType,
    MessageLane Lane,
    string ConsumerIdentity,
    MessageConsumerDispatch Dispatch
)
{
    /// <summary>Whether every process receives every message; only a Bus consumer sets it.</summary>
    public bool EveryInstance { get; init; }

    /// <summary>The generated module that declared the consumer, for conflict messages.</summary>
    public string? DeclaringModule { get; init; }

    /// <summary>The generated subscription hook of an every-instance consumer class, when it has one.</summary>
    public SubscriptionEstablishedDispatch? OnSubscriptionEstablished { get; init; }
}
