// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging;

/// <summary>
/// Provides shared consumer registration configuration for a message intent lane.
/// </summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
/// <typeparam name="TBuilder">
/// The concrete builder interface returned from each fluent call. The self-referential
/// type parameter keeps the lane-specific type (<see cref="IBusConsumerBuilder{TConsumer}"/> /
/// <see cref="IQueueConsumerBuilder{TConsumer}"/>) available through the whole chain, so future
/// lane-specific knobs remain reachable after a shared call such as <see cref="Group"/>.
/// </typeparam>
[PublicAPI]
public interface IConsumerBuilderBase<TConsumer, out TBuilder>
    where TConsumer : class
    where TBuilder : IConsumerBuilderBase<TConsumer, TBuilder>
{
    /// <summary>Sets the consumer group name for this consumer registration.</summary>
    TBuilder Group(string group);

    /// <summary>Limits the number of messages consumed concurrently by this consumer.</summary>
    TBuilder Concurrency(byte maxConcurrent);

    /// <summary>Overrides the deterministic handler identity for this consumer registration.</summary>
    TBuilder HandlerId(string handlerId);

    /// <summary>Configures per-consumer circuit breaker overrides for this registration.</summary>
    TBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure);
}

/// <summary>Configures a broadcast bus consumer registration.</summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
[PublicAPI]
public interface IBusConsumerBuilder<TConsumer> : IConsumerBuilderBase<TConsumer, IBusConsumerBuilder<TConsumer>>
    where TConsumer : class;

/// <summary>Configures a point-to-point queue consumer registration.</summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
[PublicAPI]
public interface IQueueConsumerBuilder<TConsumer> : IConsumerBuilderBase<TConsumer, IQueueConsumerBuilder<TConsumer>>
    where TConsumer : class;

internal sealed class BusConsumerBuilder<TConsumer>(MessageConsumerRegistrationBuilder registration)
    : ConsumerBuilderBase<TConsumer, IBusConsumerBuilder<TConsumer>>(registration),
        IBusConsumerBuilder<TConsumer>
    where TConsumer : class;

internal sealed class QueueConsumerBuilder<TConsumer>(MessageConsumerRegistrationBuilder registration)
    : ConsumerBuilderBase<TConsumer, IQueueConsumerBuilder<TConsumer>>(registration),
        IQueueConsumerBuilder<TConsumer>
    where TConsumer : class;

internal abstract class ConsumerBuilderBase<TConsumer, TBuilder>(MessageConsumerRegistrationBuilder registration)
    : IConsumerBuilderBase<TConsumer, TBuilder>
    where TConsumer : class
    where TBuilder : class, IConsumerBuilderBase<TConsumer, TBuilder>
{
    public TBuilder Group(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        registration.Group = group;
        return _Self;
    }

    public TBuilder Concurrency(byte maxConcurrent)
    {
        Argument.IsPositive(maxConcurrent, "Concurrency must be greater than 0");

        registration.Concurrency = maxConcurrent;
        return _Self;
    }

    public TBuilder HandlerId(string handlerId)
    {
        Argument.IsNotNullOrWhiteSpace(handlerId);

        registration.HandlerId = handlerId;
        return _Self;
    }

    public TBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        Argument.IsNotNull(configure);

        var options = new ConsumerCircuitBreakerOptions();
        configure(options);
        registration.CircuitBreakerOverride = options;

        return _Self;
    }

    // The concrete builder always implements TBuilder, so this is a safe self-cast that keeps
    // the lane interface flowing through the fluent chain without duplicating the four methods.
    private TBuilder _Self => (TBuilder)(object)this;
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
