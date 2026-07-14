// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging.Registration;

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
    /// <param name="group">A non-whitespace group name (Kafka group.id or RabbitMQ queue name).</param>
    /// <returns>The same builder instance for chaining.</returns>
    TBuilder Group(string group);

    /// <summary>Limits the number of messages consumed concurrently by this consumer.</summary>
    /// <param name="maxConcurrent">Maximum concurrent deliveries; must be greater than zero.</param>
    /// <returns>The same builder instance for chaining.</returns>
    TBuilder Concurrency(byte maxConcurrent);

    /// <summary>Overrides the deterministic handler identity for this consumer registration.</summary>
    /// <param name="handlerId">An explicit, stable handler identity string used for diagnostics and group generation.</param>
    /// <returns>The same builder instance for chaining.</returns>
    TBuilder HandlerId(string handlerId);

    /// <summary>Configures per-consumer circuit breaker overrides for this registration.</summary>
    /// <param name="configure">A callback that mutates a <see cref="ConsumerCircuitBreakerOptions"/> instance for this consumer.</param>
    /// <returns>The same builder instance for chaining.</returns>
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
    : IConsumerBuilderBase<TConsumer, TBuilder>,
        IConsumerProviderConfigBuilder
    where TConsumer : class
    where TBuilder : class, IConsumerBuilderBase<TConsumer, TBuilder>
{
    public TBuilder Group(string group)
    {
        registration.SetGroup(group);
        return Self;
    }

    public TBuilder Concurrency(byte maxConcurrent)
    {
        registration.SetConcurrency(maxConcurrent);
        return Self;
    }

    public TBuilder HandlerId(string handlerId)
    {
        registration.SetHandlerId(handlerId);
        return Self;
    }

    public TBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        registration.SetCircuitBreaker(configure);
        return Self;
    }

    void IConsumerProviderConfigBuilder.SetConsumerProviderConfig(object config) =>
        registration.SetProviderConfig(config);

    // The concrete builder always implements TBuilder, so this is a safe self-cast that keeps
    // the lane interface flowing through the fluent chain without duplicating the four methods.
    private TBuilder Self => (TBuilder)(object)this;
}

internal sealed class MessageConsumerRegistrationBuilder(
    Type consumerType,
    IntentType intentType,
    bool isAssemblyScan = false
)
{
    private readonly ProviderConfigBag _providerConfigs = new();

    public IntentType IntentType { get; set; } = intentType;

    public string? Group { get; private set; }

    public byte Concurrency { get; private set; } = 1;

    public string? HandlerId { get; private set; }

    public ConsumerCircuitBreakerOptions? CircuitBreakerOverride { get; private set; }

    public void SetGroup(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        Group = group;
    }

    public void SetConcurrency(byte maxConcurrent)
    {
        Argument.IsPositive(maxConcurrent, "Concurrency must be greater than 0");

        Concurrency = maxConcurrent;
    }

    public void SetHandlerId(string handlerId)
    {
        Argument.IsNotNullOrWhiteSpace(handlerId);

        HandlerId = handlerId;
    }

    public void SetCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        Argument.IsNotNull(configure);

        var options = new ConsumerCircuitBreakerOptions();
        configure(options);
        CircuitBreakerOverride = options;
    }

    public void SetProviderConfig(object config) => _providerConfigs.Set(config);

    public MessageConsumerRegistration Build(IReadOnlyDictionary<Type, object>? messageProviderConfigs = null)
    {
        return new MessageConsumerRegistration(
            consumerType,
            IntentType,
            isAssemblyScan,
            Group,
            Concurrency,
            HandlerId,
            CircuitBreakerOverride,
            _providerConfigs.BuildOverlay(messageProviderConfigs ?? new Dictionary<Type, object>())
        );
    }
}
