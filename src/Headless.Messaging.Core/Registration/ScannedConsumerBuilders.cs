// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging;

/// <summary>
/// Describes a consumer discovered by assembly scanning before it is registered.
/// </summary>
[PublicAPI]
public readonly record struct ScannedConsumerContext
{
    public ScannedConsumerContext(Type consumerType, Type messageType)
    {
        Argument.IsNotNull(consumerType);
        Argument.IsNotNull(messageType);

        ConsumerType = consumerType;
        MessageType = messageType;
    }

    /// <summary>The concrete consumer implementation type discovered by scanning.</summary>
    public Type ConsumerType { get; }

    /// <summary>The closed message type consumed by <see cref="ConsumerType"/>.</summary>
    public Type MessageType { get; }
}

/// <summary>
/// Configures a consumer discovered by assembly scanning.
/// </summary>
[PublicAPI]
public interface IScannedConsumerBuilder
{
    /// <summary>Registers the scanned consumer for broadcast bus delivery.</summary>
    IScannedConsumerBuilder OnBus();

    /// <summary>Registers the scanned consumer for point-to-point queue delivery.</summary>
    IScannedConsumerBuilder OnQueue();

    /// <summary>Sets the consumer group name for this scanned consumer registration.</summary>
    IScannedConsumerBuilder Group(string group);

    /// <summary>Limits the number of messages consumed concurrently by this scanned consumer.</summary>
    IScannedConsumerBuilder Concurrency(byte maxConcurrent);

    /// <summary>Overrides the deterministic handler identity for this scanned consumer registration.</summary>
    IScannedConsumerBuilder HandlerId(string handlerId);

    /// <summary>Configures per-consumer circuit breaker overrides for this scanned registration.</summary>
    IScannedConsumerBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure);

    /// <summary>Excludes this scanned consumer from both message registration and dependency injection.</summary>
    IScannedConsumerBuilder Skip();
}

internal sealed class ScannedConsumerBuilder(Type consumerType) : IScannedConsumerBuilder
{
    private readonly MessageConsumerRegistrationBuilder _registration = new(
        consumerType,
        IntentType.Bus,
        isAssemblyScan: true
    );

    public bool IsSkipped { get; private set; }

    public IScannedConsumerBuilder OnBus()
    {
        _registration.IntentType = IntentType.Bus;
        return this;
    }

    public IScannedConsumerBuilder OnQueue()
    {
        _registration.IntentType = IntentType.Queue;
        return this;
    }

    public IScannedConsumerBuilder Group(string group)
    {
        _registration.SetGroup(group);
        return this;
    }

    public IScannedConsumerBuilder Concurrency(byte maxConcurrent)
    {
        _registration.SetConcurrency(maxConcurrent);
        return this;
    }

    public IScannedConsumerBuilder HandlerId(string handlerId)
    {
        _registration.SetHandlerId(handlerId);
        return this;
    }

    public IScannedConsumerBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        _registration.SetCircuitBreaker(configure);
        return this;
    }

    public IScannedConsumerBuilder Skip()
    {
        IsSkipped = true;
        return this;
    }

    public MessageConsumerRegistration Build() => _registration.Build();
}
