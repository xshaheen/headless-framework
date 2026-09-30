// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging.Registration;

internal sealed record MessageRegistration(
    Type MessageType,
    MessageLane Lane,
    string? MessageName,
    Func<object, string?>? CorrelationSelector,
    IReadOnlyDictionary<Type, object> ProviderConfigs,
    IReadOnlyList<MessageConsumerRegistration> Consumers,
    string ContractVersion = MessageOptions.InitialContractVersion,
    bool RequiresRoutingAffinity = false,
    // Only an explicit ForMessage<T> registration carries a policy; assembly-scan and framework contributions leave
    // it null so a publish for their type falls through to the host default.
    DeliveryMode? DeliveryMode = null,
    // False for consumer-only registrations (assembly scans and framework consumers). They carry no message-level
    // settings, so they may join the one declaring registration a message type is allowed per lane.
    bool DeclaresMessage = true
);

internal sealed record MessageConsumerRegistration(
    Type ConsumerType,
    MessageLane Lane,
    bool IsAssemblyScan,
    string? Group,
    byte Concurrency,
    string? HandlerId,
    string? ConsumerIdentity,
    ConsumerCircuitBreakerOptions? CircuitBreakerOverride,
    IReadOnlyDictionary<Type, object> ProviderConfigs,
    TimeSpan? InboxRetention = null
)
{
    /// <summary>The generated dispatch of an attribute-declared consumer; null for every other registration.</summary>
    public MessageConsumerDispatch? Dispatch { get; init; }

    /// <summary>Whether every process receives every message; only an attribute-declared Bus consumer sets it.</summary>
    public bool EveryInstance { get; init; }

    /// <summary>The failure policy type an attribute-declared consumer names, if any.</summary>
    public Type? FailurePolicy { get; init; }

    /// <summary>The generated module that declared the consumer, for conflict messages; null outside modules.</summary>
    public string? DeclaringModule { get; init; }
}
