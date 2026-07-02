// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging.Registration;

internal sealed record MessageRegistration(
    Type MessageType,
    string? MessageName,
    Func<object, string?>? CorrelationSelector,
    IReadOnlyDictionary<Type, object> ProviderConfigs,
    IReadOnlyList<MessageConsumerRegistration> Consumers
);

internal sealed record MessageConsumerRegistration(
    Type ConsumerType,
    IntentType IntentType,
    bool IsAssemblyScan,
    string? Group,
    byte Concurrency,
    string? HandlerId,
    ConsumerCircuitBreakerOptions? CircuitBreakerOverride,
    IReadOnlyDictionary<Type, object> ProviderConfigs
);
