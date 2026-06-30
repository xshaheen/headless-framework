// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging.Registration;

/// <summary>
/// Describes a consumer discovered by assembly scanning before it is registered.
/// </summary>
[PublicAPI]
public sealed record ScannedConsumerContext
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
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder OnBus();

    /// <summary>Registers the scanned consumer for point-to-point queue delivery.</summary>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder OnQueue();

    /// <summary>Sets the consumer group name for this scanned consumer registration.</summary>
    /// <param name="group">A non-whitespace group name (Kafka group.id or RabbitMQ queue name).</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder Group(string group);

    /// <summary>Limits the number of messages consumed concurrently by this scanned consumer.</summary>
    /// <param name="maxConcurrent">Maximum concurrent deliveries; must be greater than zero.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder Concurrency(byte maxConcurrent);

    /// <summary>Overrides the deterministic handler identity for this scanned consumer registration.</summary>
    /// <param name="handlerId">An explicit, stable handler identity string used for diagnostics and group generation.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder HandlerId(string handlerId);

    /// <summary>Configures per-consumer circuit breaker overrides for this scanned registration.</summary>
    /// <param name="configure">A callback that mutates a <see cref="ConsumerCircuitBreakerOptions"/> instance for this consumer.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure);

    /// <summary>
    /// Excludes this scanned consumer from both message registration and dependency injection.
    /// </summary>
    /// <returns>The same builder instance for chaining.</returns>
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
