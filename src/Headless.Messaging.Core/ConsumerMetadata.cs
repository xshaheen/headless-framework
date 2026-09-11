// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging;

/// <summary>Contains metadata about a registered message consumer.</summary>
/// <param name="MessageType">The type of message this consumer handles.</param>
/// <param name="ConsumerType">The type of the consumer implementation.</param>
/// <param name="MessageName">The message name to subscribe to.</param>
/// <param name="Group">The consumer group name (Kafka group.id or RabbitMQ queue name).</param>
/// <param name="Concurrency">The maximum number of messages to process concurrently.</param>
/// <param name="Lane">The delivery lane used to subscribe this consumer.</param>
/// <param name="ConsumerIdentity">The operator-stable identity used by durable inbox state.</param>
/// <param name="MessageContractVersion">The schema version of the message contract.</param>
/// <param name="HandlerId">The deterministic handler identity used for diagnostics and default group generation.</param>
/// <remarks>
/// This record stores the configuration metadata for a consumer registered via
/// <c>ForMessage&lt;TMessage&gt;(...)</c> or assembly scanning.
/// </remarks>
[PublicAPI]
public sealed record ConsumerMetadata(
    Type MessageType,
    Type ConsumerType,
    string MessageName,
    string? Group,
    byte Concurrency,
    MessageLane Lane,
    string ConsumerIdentity,
    string MessageContractVersion,
    string? HandlerId = null
)
{
    /// <summary>Maximum supported consumer identity length for durable inbox storage.</summary>
    public const int ConsumerIdentityMaxLength = 200;

    private static readonly IReadOnlyDictionary<Type, object> _EmptyProviderConfigs = new Dictionary<Type, object>();

    /// <summary>
    /// Gets the resolved handler identity used by the runtime when an explicit id is not supplied.
    /// </summary>
    public string ResolvedHandlerId =>
        string.IsNullOrWhiteSpace(HandlerId)
            ? MessagingConventions.GetDefaultHandlerId(ConsumerType, MessageType)
            : HandlerId;

    /// <summary>
    /// Per-consumer circuit breaker overrides registered via
    /// lane-owned <c>ForMessage&lt;TMessage&gt;(...).Consumer&lt;TConsumer&gt;(...)</c>. Applied to the
    /// <see cref="ConsumerCircuitBreakerRegistry"/> during startup discovery.
    /// </summary>
    internal ConsumerCircuitBreakerOptions? CircuitBreakerOverride { get; init; }

    /// <summary>
    /// Provider-specific consumer configuration registered through provider escape hatches.
    /// </summary>
    internal IReadOnlyDictionary<Type, object> ProviderConfigs { get; init; } = _EmptyProviderConfigs;

    /// <summary>Terminal retention captured into each newly admitted inbox generation.</summary>
    public TimeSpan InboxRetention { get; init; } = TimeSpan.FromDays(30);
}
