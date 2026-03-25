// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using FluentValidation;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Kafka;

/// <summary>
/// Provides programmatic configuration for the messaging kafka project.
/// </summary>
public sealed class MessagingKafkaOptions
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
    public required string Servers { get; set; }

    /// <summary>
    /// If you need to get offset and partition and so on, you can use this function to write additional header into
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
    public List<ErrorCode> RetriableErrorCodes { get; set; } = GetDefaultRetriableErrorCodes();

    public KafkaTopicOptions TopicOptions { get; set; } = new();

    internal string GetSanitizedServersForDisplay()
    {
        return string.Join(
            ",",
            Servers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(_SanitizeServerForDisplay)
        );
    }

    private static string _SanitizeServerForDisplay(string server)
    {
        if (Uri.TryCreate(server, UriKind.Absolute, out var absoluteUri) && !string.IsNullOrEmpty(absoluteUri.UserInfo))
        {
            var builder = new UriBuilder(absoluteUri) { UserName = string.Empty, Password = string.Empty };
            var sanitized = builder.Uri.GetLeftPart(UriPartial.Authority);

            if (absoluteUri.PathAndQuery is { Length: > 1 })
            {
                sanitized += absoluteUri.PathAndQuery;
            }

            if (!string.IsNullOrEmpty(absoluteUri.Fragment))
            {
                sanitized += absoluteUri.Fragment;
            }

            return sanitized;
        }

        if (
            !server.Contains("://", StringComparison.Ordinal)
            && server.Contains('@')
            && Uri.TryCreate("kafka://" + server, UriKind.Absolute, out var inferredUri)
        )
        {
            return inferredUri.IsDefaultPort ? inferredUri.Host : $"{inferredUri.Host}:{inferredUri.Port}";
        }

        return server;
    }

    public static List<ErrorCode> GetDefaultRetriableErrorCodes()
    {
        return
        [
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
        ];
    }
}

public sealed class KafkaTopicOptions
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

internal sealed class MessagingKafkaOptionsValidator : AbstractValidator<MessagingKafkaOptions>
{
    public MessagingKafkaOptionsValidator()
    {
        RuleFor(x => x.Servers).NotEmpty();
        RuleFor(x => x.ConnectionPoolSize).GreaterThan(0);
    }
}
