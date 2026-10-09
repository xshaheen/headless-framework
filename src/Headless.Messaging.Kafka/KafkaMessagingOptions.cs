// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using FluentValidation;

namespace Headless.Messaging.Kafka;

/// <summary>
/// Configuration options for the Apache Kafka messaging transport.
/// </summary>
public sealed class KafkaMessagingOptions
{
    /// <summary>
    /// Raw librdkafka configuration key/value pairs that are merged into the producer and
    /// consumer configuration before connecting. Topic-level parameters use the
    /// <c>default.topic.config</c> sub-key.
    /// See <see href="https://github.com/edenhill/librdkafka/blob/master/CONFIGURATION.md"/> for the
    /// full parameter reference.
    /// </summary>
    /// <remarks>
    /// The producer enables <c>enable.idempotence</c> unless this sets it, or sets <c>acks</c> other than
    /// <c>all</c>, more than five in-flight requests, zero retries, or a non-FIFO <c>queuing.strategy</c>, which
    /// librdkafka cannot combine with idempotence (under either name librdkafka accepts for each setting). The
    /// consumer uses <c>cooperative-sticky</c> assignment unless this sets <c>partition.assignment.strategy</c>.
    /// The transport owns offset commits, so <c>enable.auto.commit</c> and <c>enable.auto.offset.store</c> are
    /// always overridden; <c>auto.commit.interval.ms</c> still sets how often stored offsets reach the broker.
    /// </remarks>
    public Dictionary<string, string> MainConfig { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The <c>bootstrap.servers</c> value — a comma-separated list of broker <c>host</c> or
    /// <c>host:port</c> addresses used to establish the initial connection to the cluster.
    /// </summary>
    public required string Servers { get; set; }

    /// <summary>
    /// Optional callback that adds extra headers to an inbound message from native Kafka metadata.
    /// Use this to surface partition, offset, topic, or timestamp information as framework message headers.
    /// </summary>
    public Func<
        ConsumeResult<string, byte[]>,
        IServiceProvider,
        List<KeyValuePair<string, string>>
    >? CustomHeadersBuilder { get; set; }

    /// <summary>
    /// The set of Kafka error codes that trigger a consume retry, expressed as the integer values of
    /// Confluent's <c>Confluent.Kafka.ErrorCode</c> enum. Exposing <see langword="int"/> instead of the native enum
    /// keeps configuring retries free of a compile-time <c>Confluent.Kafka</c> dependency. The defaults
    /// include transient errors such as leader elections, rebalances, and network timeouts.
    /// See <see href="https://docs.confluent.io/platform/current/clients/librdkafka/html/rdkafkacpp_8h.html#a4c6b7af48c215724c323c60ea4080dbf"/>
    /// for the enum members and their numeric codes.
    /// </summary>
    public List<int> RetriableErrorCodes { get; } = [.. DefaultRetriableErrorCodes];

    /// <summary>
    /// Topic creation options applied when the framework auto-creates topics.
    /// </summary>
    public KafkaTopicOptions TopicOptions { get; set; } = new();

    /// <summary>
    /// Returns the default set of retriable Kafka error codes as the integer values of the
    /// <c>Confluent.Kafka.ErrorCode</c> enum members.
    /// </summary>
    public static IReadOnlyList<int> DefaultRetriableErrorCodes { get; } =
    [
        (int)ErrorCode.GroupLoadInProgress,
        (int)ErrorCode.Local_Retry,
        (int)ErrorCode.Local_TimedOut,
        (int)ErrorCode.RequestTimedOut,
        (int)ErrorCode.LeaderNotAvailable,
        (int)ErrorCode.NotLeaderForPartition,
        (int)ErrorCode.RebalanceInProgress,
        (int)ErrorCode.NotCoordinatorForGroup,
        (int)ErrorCode.NetworkException,
        (int)ErrorCode.GroupCoordinatorNotAvailable,
    ];
}

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

internal sealed class KafkaMessagingOptionsValidator : AbstractValidator<KafkaMessagingOptions>
{
    public KafkaMessagingOptionsValidator()
    {
        RuleFor(x => x.Servers).NotEmpty();
    }
}
