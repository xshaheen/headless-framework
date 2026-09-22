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

    /// <summary>Overrides the deterministic handler identity for diagnostics and default group generation.</summary>
    /// <param name="handlerId">An explicit handler identity string; this is not the durable inbox identity.</param>
    /// <returns>The same builder instance for chaining.</returns>
    TBuilder HandlerId(string handlerId);

    /// <summary>Sets the operator-stable identity used by the durable inbox.</summary>
    /// <param name="consumerIdentity">Nonblank identity of at most <see cref="ConsumerMetadata.ConsumerIdentityMaxLength"/> characters that remains unchanged across handler and topology refactors.</param>
    /// <returns>The same builder instance for chaining.</returns>
    TBuilder ConsumerIdentity(string consumerIdentity);

    /// <summary>Overrides the terminal inbox retention captured for future generations.</summary>
    /// <param name="retention">A positive whole-second duration no greater than <see cref="int.MaxValue"/> seconds.</param>
    /// <returns>The same builder instance for chaining.</returns>
    TBuilder InboxRetention(TimeSpan retention);

    /// <summary>Configures per-consumer circuit breaker overrides for this registration.</summary>
    /// <param name="configure">A callback that mutates a <see cref="ConsumerCircuitBreakerOptions"/> instance for this consumer.</param>
    /// <returns>The same builder instance for chaining.</returns>
    TBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure);
}

/// <summary>Configures a broadcast bus consumer registration.</summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
[PublicAPI]
public interface IBusConsumerBuilder<TConsumer> : IConsumerBuilderBase<TConsumer, IBusConsumerBuilder<TConsumer>>
    where TConsumer : class
{
    /// <summary>
    /// Delivers a copy to every running instance instead of one copy to the group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Bus subscription delivers one copy per consumer group, and instances sharing a group compete for it.
    /// That is right for work, and wrong for a consumer that maintains per-process state — an in-memory cache
    /// tier, a resolved policy held in a field — because the instances that lose the race keep serving what they
    /// already hold. This qualifies the group with the instance identity so every process gets its own copy.
    /// </para>
    /// <para>
    /// The cost is one broker-side subscription per instance identity
    /// (<c>IHostIdentityAccessor.HostName</c>: the pod name under Kubernetes, the machine name otherwise). Those
    /// are not reaped when an instance goes away, so an environment that churns instance names — a Deployment
    /// rollout, which renames pods — accumulates them until an operator removes them. Prefer it for signals that
    /// refresh process-local state; never use it for work that must happen once.
    /// </para>
    /// </remarks>
    /// <returns>The same builder instance for chaining.</returns>
    IBusConsumerBuilder<TConsumer> PerInstance();
}

/// <summary>Configures a point-to-point queue consumer registration.</summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
[PublicAPI]
public interface IQueueConsumerBuilder<TConsumer> : IConsumerBuilderBase<TConsumer, IQueueConsumerBuilder<TConsumer>>
    where TConsumer : class;

internal sealed class BusConsumerBuilder<TConsumer>(MessageConsumerRegistrationBuilder registration)
    : ConsumerBuilderBase<TConsumer, IBusConsumerBuilder<TConsumer>>(registration),
        IBusConsumerBuilder<TConsumer>
    where TConsumer : class
{
    public IBusConsumerBuilder<TConsumer> PerInstance()
    {
        Registration.SetPerInstance();
        return this;
    }
}

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
    /// <summary>The registration this builder mutates, exposed so lane-specific builders reuse it rather than
    /// capturing the primary-constructor parameter a second time.</summary>
    protected MessageConsumerRegistrationBuilder Registration => registration;

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

    public TBuilder ConsumerIdentity(string consumerIdentity)
    {
        registration.SetConsumerIdentity(consumerIdentity);
        return Self;
    }

    public TBuilder InboxRetention(TimeSpan retention)
    {
        registration.SetInboxRetention(retention);
        return Self;
    }

    public TBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        registration.SetCircuitBreaker(configure);
        return Self;
    }

    void IConsumerProviderConfigBuilder.SetConsumerProviderConfig(object config)
    {
        registration.SetProviderConfig(config);
    }

    // The concrete builder always implements TBuilder, so this is a safe self-cast that keeps
    // the lane interface flowing through the fluent chain without duplicating the shared fluent methods.
    private TBuilder Self => (TBuilder)(object)this;
}

internal sealed class MessageConsumerRegistrationBuilder(
    Type consumerType,
    MessageLane lane,
    bool isAssemblyScan = false
)
{
    private readonly ProviderConfigBag _providerConfigs = new();

    public MessageLane Lane { get; } = lane;

    public string? Group { get; private set; }

    public byte Concurrency { get; private set; } = 1;

    public string? HandlerId { get; private set; }

    public string? ConsumerIdentity { get; private set; }

    public TimeSpan? InboxRetention { get; private set; }

    public ConsumerCircuitBreakerOptions? CircuitBreakerOverride { get; private set; }

    public bool PerInstance { get; private set; }

    public void SetGroup(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        Group = group;
    }

    public void SetPerInstance()
    {
        PerInstance = true;
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

    public void SetConsumerIdentity(string consumerIdentity)
    {
        Argument.IsNotNullOrWhiteSpace(consumerIdentity);
        Argument.HasMaxLength(consumerIdentity, ConsumerMetadata.ConsumerIdentityMaxLength);

        ConsumerIdentity = consumerIdentity;
    }

    public void SetInboxRetention(TimeSpan retention)
    {
        const string message =
            "Inbox retention must be a positive whole-second duration no greater than Int32.MaxValue seconds.";
        Argument.IsPositive(retention, message);
        Argument.IsZero(retention.Ticks % TimeSpan.TicksPerSecond, message, nameof(retention));
        Argument.IsLessThanOrEqualTo(retention.TotalSeconds, int.MaxValue, message, nameof(retention));

        InboxRetention = retention;
    }

    public void SetCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        Argument.IsNotNull(configure);

        var options = new ConsumerCircuitBreakerOptions();
        configure(options);
        CircuitBreakerOverride = options;
    }

    public void SetProviderConfig(object config)
    {
        _providerConfigs.Set(config);
    }

    public MessageConsumerRegistration Build(IReadOnlyDictionary<Type, object>? messageProviderConfigs = null)
    {
        return new MessageConsumerRegistration(
            consumerType,
            Lane,
            isAssemblyScan,
            Group,
            Concurrency,
            HandlerId,
            ConsumerIdentity,
            CircuitBreakerOverride,
            _providerConfigs.BuildOverlay(messageProviderConfigs ?? new Dictionary<Type, object>()),
            InboxRetention,
            PerInstance
        );
    }
}
