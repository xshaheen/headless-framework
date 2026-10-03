// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using FluentValidation;

namespace Headless.Messaging.Kafka;

/// <summary>Topic creation settings used when the framework auto-creates Kafka topics.</summary>
public sealed class KafkaTopicOptions
{
    /// <summary>
    /// The number of partitions for an auto-created topic. <c>-1</c> (default) delegates
    /// the decision to the broker's configured default.
    /// </summary>
    public short NumPartitions { get; set; } = -1;

    /// <summary>
    /// The replication factor for an auto-created topic. <c>-1</c> (default) delegates
    /// the decision to the broker's configured default.
    /// </summary>
    public short ReplicationFactor { get; set; } = -1;
}
