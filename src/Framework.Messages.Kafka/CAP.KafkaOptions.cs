// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Framework.Messages.Messages;

namespace Framework.Messages;

/// <summary>
/// Provides programmatic configuration for the CAP kafka project.
/// </summary>
public class KafkaOptions
{
    /// <summary>
    /// librdkafka configuration parameters (refer to https://github.com/edenhill/librdkafka/blob/master/CONFIGURATION.md).
    /// <para>
    /// Topic configuration parameters are specified via the "default.topic.config" sub-dictionary config parameter.
    /// </para>
    /// </summary>
    public Dictionary<string, string> MainConfig { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Producer connection pool size, default is 10
    /// </summary>
    public int ConnectionPoolSize { get; set; } = 10;

    /// <summary>
    /// The `bootstrap.servers` item config of <see cref="MainConfig" />.
    /// <para>
    /// Initial list of brokers as a CSV list of broker host or host:port.
    /// </para>
    /// </summary>
    public string Servers { get; set; } = default!;

    /// <summary>
    /// If you need to get offset and partition and so on.., you can use this function to write additional header into
    /// <see cref="MessageHeader" />
    /// </summary>
    public Func<
        ConsumeResult<string, byte[]>,
        IServiceProvider,
        List<KeyValuePair<string, string>>
    >? CustomHeadersBuilder { get; set; }

    /// <summary>
    /// New retriable error code (refer to
    /// https://docs.confluent.io/platform/current/clients/librdkafka/html/rdkafkacpp_8h.html#a4c6b7af48c215724c323c60ea4080dbf)
    /// </summary>
    public IList<ErrorCode> RetriableErrorCodes { get; set; } =
        new List<ErrorCode>
        {
            ErrorCode.GroupLoadInProgress,
            ErrorCode.Local_Retry,
            ErrorCode.Local_TimedOut,
            ErrorCode.RequestTimedOut,
            ErrorCode.LeaderNotAvailable,
            ErrorCode.NotLeaderForPartition,
            ErrorCode.RebalanceInProgress,
            ErrorCode.NotCoordinatorForGroup,
            ErrorCode.NetworkException,
            ErrorCode.GroupCoordinatorNotAvailable,
        };

    public KafkaTopicOptions TopicOptions { get; set; } = new();
}

public class KafkaTopicOptions
{
    /// <summary>
    /// The number of partitions for the new topic
    /// </summary>
    public short NumPartitions { get; set; } = -1;

    /// <summary>
    /// The replication factor for the new topic
    /// </summary>
    public short ReplicationFactor { get; set; } = -1;
}
