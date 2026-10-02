// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging;

/// <summary>Contains metadata about a registered message consumer.</summary>
/// <param name="MessageType">The type of message this consumer handles.</param>
/// <param name="ConsumerType">The type of the consumer implementation.</param>
/// <param name="MessageName">The message name to subscribe to.</param>
/// <param name="Concurrency">The maximum number of messages to process concurrently.</param>
/// <param name="Lane">The delivery lane used to subscribe this consumer.</param>
/// <param name="ConsumerIdentity">The consumer identity its attribute declares, which keys durable inbox state.</param>
/// <param name="MessageContractVersion">The schema version of the message contract.</param>
/// <remarks>
/// A consumer declared with <see cref="BusConsumerAttribute"/> or <see cref="QueueConsumerAttribute"/> has one entry per
/// message it consumes.
/// </remarks>
[PublicAPI]
public sealed record ConsumerMetadata(
    Type MessageType,
    Type ConsumerType,
    string MessageName,
    byte Concurrency,
    MessageLane Lane,
    string ConsumerIdentity,
    string MessageContractVersion
)
{
    /// <summary>Maximum supported consumer identity length for durable inbox storage.</summary>
    public const int ConsumerIdentityMaxLength = MessagingCatalogBuilder.ConsumerIdentityMaxLength;

    private static readonly IReadOnlyDictionary<Type, object> _EmptyProviderConfigs = new Dictionary<Type, object>();

    /// <summary>
    /// The broker subscription this consumer's client opens: its identity on the Bus lane, so one client binds every
    /// message the identity covers, and its message name on the Queue lane, which has one consumer per message.
    /// </summary>
    public string SubscriptionName => Lane == MessageLane.Bus ? ConsumerIdentity : MessageName;

    /// <summary>
    /// The circuit breaker overrides <c>Tune</c> or configuration gave this consumer. Applied to the
    /// <see cref="ConsumerCircuitBreakerRegistry"/> when the consumer registry is built.
    /// </summary>
    internal ConsumerCircuitBreakerOptions? CircuitBreakerOverride { get; init; }

    /// <summary>Provider-specific consumer configuration that <c>Tune</c> gave this consumer.</summary>
    internal IReadOnlyDictionary<Type, object> ProviderConfigs { get; init; } = _EmptyProviderConfigs;

    /// <summary>Terminal retention captured into each newly admitted inbox generation.</summary>
    public TimeSpan InboxRetention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether this consumer receives every Bus message in every process, as its <see cref="BusConsumerAttribute"/>
    /// declares. Always <see langword="false"/> on the Queue lane.
    /// </summary>
    public bool EveryInstance { get; init; }

    /// <summary>The generated dispatch that runs the consumer class.</summary>
    internal MessageConsumerDispatch? Dispatch { get; init; }

    /// <summary>
    /// The generated <see cref="IOnSubscriptionEstablished"/> call of an every-instance consumer class that implements
    /// the hook, or <see langword="null"/>.
    /// </summary>
    internal SubscriptionEstablishedDispatch? OnSubscriptionEstablished { get; init; }

    /// <summary>The generated module that declared the consumer, or <see langword="null"/> outside modules.</summary>
    internal string? DeclaringModule { get; init; }

    /// <summary>
    /// Consume middleware types that <c>Tune</c> attached to this consumer alone, in the order they run inside the global
    /// and per-message middleware.
    /// </summary>
    internal IReadOnlyList<Type> Middleware { get; init; } = [];
}
