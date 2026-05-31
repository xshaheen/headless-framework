// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging;

/// <summary>
/// Provides shared consumer registration configuration for a message intent lane.
/// </summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
[PublicAPI]
public interface IConsumerBuilderBase<TConsumer>
    where TConsumer : class
{
    /// <summary>Sets the consumer group name for this consumer registration.</summary>
    IConsumerBuilderBase<TConsumer> Group(string group);

    /// <summary>Limits the number of messages consumed concurrently by this consumer.</summary>
    IConsumerBuilderBase<TConsumer> Concurrency(byte maxConcurrent);

    /// <summary>Overrides the deterministic handler identity for this consumer registration.</summary>
    IConsumerBuilderBase<TConsumer> HandlerId(string handlerId);

    /// <summary>Configures per-consumer circuit breaker overrides for this registration.</summary>
    IConsumerBuilderBase<TConsumer> WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure);
}

/// <summary>Configures a broadcast bus consumer registration.</summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
[PublicAPI]
public interface IBusConsumerBuilder<TConsumer> : IConsumerBuilderBase<TConsumer>
    where TConsumer : class;

/// <summary>Configures a point-to-point queue consumer registration.</summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
[PublicAPI]
public interface IQueueConsumerBuilder<TConsumer> : IConsumerBuilderBase<TConsumer>
    where TConsumer : class;

internal sealed class BusConsumerBuilder<TConsumer>(MessageConsumerRegistrationBuilder registration)
    : ConsumerBuilderBase<TConsumer>(registration),
        IBusConsumerBuilder<TConsumer>
    where TConsumer : class;

internal sealed class QueueConsumerBuilder<TConsumer>(MessageConsumerRegistrationBuilder registration)
    : ConsumerBuilderBase<TConsumer>(registration),
        IQueueConsumerBuilder<TConsumer>
    where TConsumer : class;

internal abstract class ConsumerBuilderBase<TConsumer>(MessageConsumerRegistrationBuilder registration)
    : IConsumerBuilderBase<TConsumer>
    where TConsumer : class
{
    public IConsumerBuilderBase<TConsumer> Group(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        registration.Group = group;
        return this;
    }

    public IConsumerBuilderBase<TConsumer> Concurrency(byte maxConcurrent)
    {
        if (maxConcurrent == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), "Concurrency must be greater than 0");
        }

        registration.Concurrency = maxConcurrent;
        return this;
    }

    public IConsumerBuilderBase<TConsumer> HandlerId(string handlerId)
    {
        Argument.IsNotNullOrWhiteSpace(handlerId);

        registration.HandlerId = handlerId;
        return this;
    }

    public IConsumerBuilderBase<TConsumer> WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        Argument.IsNotNull(configure);

        var options = new ConsumerCircuitBreakerOptions();
        configure(options);
        registration.CircuitBreakerOverride = options;

        return this;
    }
}

internal sealed class MessageConsumerRegistrationBuilder(
    Type consumerType,
    IntentType intentType,
    bool isAssemblyScan = false
)
{
    public string? Group { get; set; }

    public byte Concurrency { get; set; } = 1;

    public string? HandlerId { get; set; }

    public ConsumerCircuitBreakerOptions? CircuitBreakerOverride { get; set; }

    public MessageConsumerRegistration Build()
    {
        return new MessageConsumerRegistration(
            consumerType,
            intentType,
            isAssemblyScan,
            Group,
            Concurrency,
            HandlerId,
            CircuitBreakerOverride
        );
    }
}
