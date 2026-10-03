// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Defines log event types reported by message brokers and transport implementations.
/// These events allow applications to monitor broker and consumer health, connectivity issues, and other diagnostics.
/// </summary>
public enum MqLogType
{
    /// <summary>
    /// Consumer subscription was cancelled (RabbitMQ).
    /// Typically indicates the consumer was unsubscribed or the connection was terminated.
    /// </summary>
    ConsumerCancelled = 0,

    /// <summary>
    /// Consumer successfully registered and is ready to receive messages (RabbitMQ).
    /// </summary>
    ConsumerRegistered = 1,

    /// <summary>
    /// Consumer unregistered from the message broker (RabbitMQ).
    /// </summary>
    ConsumerUnregistered = 2,

    /// <summary>
    /// Consumer connection to the broker was shut down (RabbitMQ).
    /// </summary>
    ConsumerShutdown = 3,

    /// <summary>
    /// An error occurred during message consumption (Kafka).
    /// </summary>
    ConsumeError = 4,

    /// <summary>
    /// Consumer is retrying after a consumption failure (Kafka).
    /// </summary>
    ConsumeRetries = 5,

    /// <summary>
    /// Failed to establish or maintain connection to the broker server (Kafka).
    /// </summary>
    ServerConnError = 6,

    /// <summary>
    /// An exception was received from the message broker (Azure Service Bus).
    /// </summary>
    ExceptionReceived = 7,

    /// <summary>
    /// An asynchronous error event occurred (NATS).
    /// </summary>
    AsyncErrorEvent = 8,

    /// <summary>
    /// Failed to connect or connection error occurred (NATS).
    /// </summary>
    ConnectError = 9,

    /// <summary>
    /// An invalid ID format was detected during message processing (Amazon SQS).
    /// </summary>
    InvalidIdFormat = 10,

    /// <summary>
    /// A message is not currently in flight, preventing visibility timeout change (Amazon SQS).
    /// </summary>
    MessageNotInflight = 11,

    /// <summary>
    /// An error occurred during message consumption (Redis Streams).
    /// </summary>
    RedisConsumeError = 12,

    /// <summary>
    /// The transport detected a configuration that is accepted but cannot be fully honored.
    /// </summary>
    TransportConfigurationWarning = 13,
}
