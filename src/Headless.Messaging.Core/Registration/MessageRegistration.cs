// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging.Registration;

internal sealed record MessageRegistration(
    Type MessageType,
    MessageLane Lane,
    string? MessageName,
    Func<object, string?>? CorrelationSelector,
    IReadOnlyDictionary<Type, object> ProviderConfigs,
    IReadOnlyList<MessageConsumerRegistration> Consumers
);

internal sealed record MessageConsumerRegistration(
    Type ConsumerType,
    MessageLane Lane,
    bool IsAssemblyScan,
    string? Group,
    byte Concurrency,
    string? HandlerId,
    ConsumerCircuitBreakerOptions? CircuitBreakerOverride,
    IReadOnlyDictionary<Type, object> ProviderConfigs
);
