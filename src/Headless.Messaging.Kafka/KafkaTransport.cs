// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Kafka;

internal sealed class KafkaTransport(ILogger<KafkaTransport> logger, IKafkaConnectionPool connectionPool)
    : IQueueTransport
{
    private readonly ILogger _logger = logger;

    public BrokerAddress BrokerAddress => new("kafka", connectionPool.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        var producer = connectionPool.RentProducer();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                        Key =
                            message.Headers.TryGetValue(KafkaHeaders.KafkaKey, out var kafkaMessageKey)
                            && !string.IsNullOrEmpty(kafkaMessageKey)
                                ? kafkaMessageKey
                                : message.Id,
                        Value = message.Body.ToArray(),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (result.Status is PersistenceStatus.Persisted or PersistenceStatus.PossiblyPersisted)
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
            return OperateResult.Failed(new PublisherSentFailedException(e.Message, e));
        }
        finally
        {
            connectionPool.Return(producer);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
