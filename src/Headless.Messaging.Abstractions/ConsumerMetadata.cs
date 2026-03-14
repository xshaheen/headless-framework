// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Contains metadata about a registered message consumer.
/// </summary>
/// <param name="MessageType">The type of message this consumer handles.</param>
/// <param name="ConsumerType">The type of the consumer implementation.</param>
/// <param name="Topic">The topic name to subscribe to.</param>
/// <param name="Group">The consumer group name (Kafka group.id or RabbitMQ queue name).</param>
/// <param name="Concurrency">The maximum number of messages to process concurrently.</param>
/// <param name="HandlerId">The deterministic handler identity used for duplicate detection and diagnostics.</param>
/// <remarks>
/// This record stores the configuration metadata for a consumer registered via
/// <see cref="IMessagingBuilder.SubscribeFromAssembly"/> or <see cref="IMessagingBuilder.Subscribe{T}(string)"/>.
/// </remarks>
public sealed record ConsumerMetadata(
    Type MessageType,
    Type ConsumerType,
    string Topic,
    string? Group,
    byte Concurrency,
    string? HandlerId = null
)
{
    /// <summary>
    /// Gets the resolved handler identity used by the runtime when an explicit id is not supplied.
    /// </summary>
    public string ResolvedHandlerId => string.IsNullOrWhiteSpace(HandlerId)
        ? MessagingConventions.GetDefaultHandlerId(ConsumerType, MessageType)
        : HandlerId!;
}
