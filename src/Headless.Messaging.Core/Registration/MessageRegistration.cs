// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging;

internal sealed record MessageRegistration(
    Type MessageType,
    string? MessageName,
    IReadOnlyList<MessageConsumerRegistration> Consumers
);

internal sealed record MessageConsumerRegistration(
    Type ConsumerType,
    IntentType IntentType,
    bool IsAssemblyScan,
    string? Group,
    byte Concurrency,
    string? HandlerId,
    ConsumerCircuitBreakerOptions? CircuitBreakerOverride
);
