// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging.Messages;

/// <summary>
/// Describes one subscribed consumer of one message: what the host's consumer clients subscribe to and how a delivery
/// reaches the consumer.
/// </summary>
public sealed class ConsumerExecutorDescriptor
{
    /// <summary>
    /// The class that handles the message: the attribute-declared consumer, or the type that declares a runtime
    /// subscription's delegate.
    /// </summary>
    public required Type ConsumerType { get; init; }

    /// <summary>
    /// The method that handles the message, for diagnostics: <c>ConsumeAsync</c> for an attribute-declared consumer, the
    /// delegate's method for a runtime subscription.
    /// </summary>
    public string MethodName { get; init; } = nameof(IConsume<>.ConsumeAsync);

    /// <summary>
    /// The message payload type deliveries are deserialized into, or <see langword="null"/> for an untyped consumer,
    /// whose payload stays as delivered.
    /// </summary>
    public Type? MessageType { get; init; }

    /// <summary>
    /// Message name for the consumer. Can be set directly or computed from attributes.
    /// </summary>
    public required string MessageName { get; init; }

    /// <summary>
    /// The broker subscription the consumer's client opens: the consumer identity on the Bus lane, the message name on
    /// the Queue lane, and the resolved identity of a runtime subscription.
    /// </summary>
    public required string SubscriptionName { get; init; }

    /// <summary>
    /// Maximum number of messages to process concurrently for this consumer.
    /// </summary>
    public byte Concurrency { get; init; } = 1;

    /// <summary>
    /// The deterministic identity of a runtime subscription's delegate, which routes a delivery to that delegate;
    /// <see langword="null"/> for an attribute-declared consumer.
    /// </summary>
    public string? HandlerId { get; init; }

    /// <summary>The operator-stable identity used to route persisted inbox recovery.</summary>
    public string? ConsumerIdentity { get; init; }

    /// <summary>
    /// The identity that keys this consumer's received rows, circuit breaker, metrics, and the
    /// <see cref="Headers.ConsumerIdentity"/> header: its declared identity, or the subscription name for a runtime
    /// subscription, which declares none.
    /// </summary>
    internal string ResolvedConsumerIdentity =>
        string.IsNullOrWhiteSpace(ConsumerIdentity) ? SubscriptionName : ConsumerIdentity;

    /// <summary>
    /// The circuit breaker key of this consumer's identity on its lane. Cached because every delivery reads it and the
    /// descriptor never changes after registration; the benign publication race writes an equal string.
    /// </summary>
    internal string CircuitBreakerKey => field ??= CircuitBreakerKeys.For(Lane, ResolvedConsumerIdentity);

    /// <summary>The schema version of the message contract used to isolate inbox generations.</summary>
    public string? MessageContractVersion { get; init; }

    /// <summary>Terminal retention captured when this consumer admits a new inbox generation.</summary>
    public TimeSpan InboxRetention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Delivery intent used to subscribe this consumer.
    /// </summary>
    public required MessageLane Lane { get; init; }

    /// <summary>
    /// Whether this process receives every message of this consumer through a subscription of its own, rather than
    /// competing with other processes. Always <see langword="false"/> on the Queue lane.
    /// </summary>
    public bool EveryInstance { get; init; }

    /// <summary>The subscription kind the consumer's client opens.</summary>
    internal Transport.ConsumerSubscriptionKind SubscriptionKind =>
        EveryInstance ? Transport.ConsumerSubscriptionKind.EveryInstance : Transport.ConsumerSubscriptionKind.Competing;

    /// <summary>
    /// The generated dispatch of an attribute-declared consumer, which builds the consumer class and calls the typed
    /// <see cref="IConsume{TMessage}.ConsumeAsync"/>; <see langword="null"/> for a runtime subscription.
    /// </summary>
    internal MessageConsumerDispatch? Dispatch { get; init; }

    /// <summary>
    /// The generated <see cref="IOnSubscriptionEstablished"/> call of an every-instance consumer class that implements
    /// the hook; <see langword="null"/> for any other consumer and for a runtime subscription.
    /// </summary>
    internal SubscriptionEstablishedDispatch? OnSubscriptionEstablished { get; init; }

    /// <summary>Consume middleware types that run for this consumer alone, resolved from the delivery's scope.</summary>
    internal IReadOnlyList<Type> Middleware { get; init; } = [];
}
