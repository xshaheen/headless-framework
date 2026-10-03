// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reliability;

namespace Headless.Messaging.Registration;

/// <summary>
/// One message route the consumer registry folds when it is built: either the lane half of a
/// <c>Message&lt;T&gt;(name, version)</c> contract, which declares the route's settings, or one generated consumer,
/// which joins the route of its message. Neither is registered in the container; the frozen registry holds the
/// contract routes as <see cref="ConsumerRegistry.DeclaredRoutes"/>.
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
