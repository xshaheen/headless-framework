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
    DeliveryMode? DeliveryMode = null
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
    TimeSpan? InboxRetention = null,
    bool PerInstance = false
);
