// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

/// <summary>
/// Contains metadata about a registered message consumer.
/// </summary>
/// <param name="MessageType">The type of message this consumer handles.</param>
/// <param name="ConsumerType">The type of the consumer implementation.</param>
/// <param name="Topic">The topic name to subscribe to.</param>
/// <param name="Group">The consumer group name (Kafka group.id or RabbitMQ queue name).</param>
/// <param name="Concurrency">The maximum number of messages to process concurrently.</param>
/// <remarks>
/// This record stores the configuration metadata for a consumer registered via
/// <see cref="IMessagingBuilder.Consumer{TConsumer}"/> or <see cref="IMessagingBuilder.ScanConsumers"/>.
/// The metadata is used by <see cref="IConsumerServiceSelector"/> to build consumer descriptors
/// that the messaging system uses for message routing and execution.
/// </remarks>
public sealed record ConsumerMetadata(
    Type MessageType,
    Type ConsumerType,
    string Topic,
    string? Group,
    byte Concurrency
);
