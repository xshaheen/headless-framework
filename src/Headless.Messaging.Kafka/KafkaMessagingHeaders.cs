// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Kafka;

/// <summary>Framework-defined header names used with the Apache Kafka transport.</summary>
[PublicAPI]
public static class KafkaMessagingHeaders
{
    /// <summary>
    /// Header carrying the Kafka message key, which controls partition routing.
    /// Messages with the same key are routed to the same partition and are delivered in order
    /// within that partition. Set via <c>KafkaMessageConfigBuilder.PartitionBy</c>.
    /// </summary>
    public const string KafkaKey = "headless-kafka-key";
}
