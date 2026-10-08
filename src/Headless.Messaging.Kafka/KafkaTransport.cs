// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Kafka;

internal sealed class KafkaTransport(ILogger<KafkaTransport> logger, IKafkaProducerProvider producerProvider)
    : IQueueTransport
{
    private readonly ILogger _logger = logger;

    // Set once DisposeAsync runs: a later send fails instead of reaching the broker.
    private int _disposed;

    public BrokerAddress BrokerAddress => new("kafka", producerProvider.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return OperateResult.Failed(new ObjectDisposedException(nameof(KafkaTransport)));
        }

        var affinityKey = KafkaRoutingAffinity.Mapping.ResolveKey(message);
        IProducer<string, byte[]>? producer = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            producer = producerProvider.GetProducer();

            var headers = new Confluent.Kafka.Headers();

            foreach (var header in message.Headers)
            {
                headers.Add(
                    header.Value != null
                        ? new Header(header.Key, Encoding.UTF8.GetBytes(header.Value))
                        : new Header(header.Key, value: null)
                );
            }

            var result = await producer
                .ProduceAsync(
                    message.Name,
                    new Message<string, byte[]>
                    {
                        Headers = headers,
                        Key = string.IsNullOrEmpty(affinityKey) ? message.Id : affinityKey,
                        Value = _GetMessageBodyArray(message.Body),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (result.Status is PersistenceStatus.Persisted)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogKafkaTopicMessagePublished(message.Name);
                }

                return OperateResult.Success;
            }

            throw new PublisherSentFailedException("kafka message persisted failed!");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // An idempotent producer that hits a fatal error (such as a sequence gap after unclean leader election)
            // fails every later produce, so the shared instance is replaced instead of failing until restart.
            if (producer is not null && e is KafkaException { Error.IsFatal: true })
            {
                producerProvider.DiscardFailedProducer(producer);
            }

            return OperateResult.Failed(new PublisherSentFailedException(e.Message, e));
        }
    }

    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    private static byte[] _GetMessageBodyArray(ReadOnlyMemory<byte> body)
    {
        if (
            MemoryMarshal.TryGetArray(body, out var segment)
            && segment is { Array: { } array, Offset: 0 }
            && segment.Count == array.Length
        )
        {
            return array;
        }

        return body.ToArray();
    }
}

internal static partial class KafkaTransportLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "KafkaTopicMessagePublished",
        Level = LogLevel.Debug,
        Message = "kafka topic message [{GetName}] has been published."
    )]
    public static partial void LogKafkaTopicMessagePublished(this ILogger logger, string getName);
}
