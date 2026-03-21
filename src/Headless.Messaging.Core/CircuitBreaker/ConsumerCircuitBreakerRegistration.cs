// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Represents a pending per-consumer circuit breaker registration created via
/// <see cref="IConsumerBuilder{TConsumer}.WithCircuitBreaker"/> when using the
/// <c>IServiceCollection.AddConsumer</c> extension path.
/// Instances are registered as singletons and applied to <see cref="ConsumerCircuitBreakerRegistry"/>
/// during messaging startup.
/// </summary>
internal sealed record ConsumerCircuitBreakerRegistration(
    Type ConsumerType,
    Type MessageType,
    string? GroupName,
    ConsumerCircuitBreakerOptions Options
);
