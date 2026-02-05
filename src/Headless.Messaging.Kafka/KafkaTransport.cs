// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Headers = Confluent.Kafka.Headers;

namespace Headless.Messaging.Kafka;

internal sealed class KafkaTransport(ILogger<KafkaTransport> logger, IKafkaConnectionPool connectionPool) : ITransport
{
    private readonly ILogger _logger = logger;

    public BrokerAddress BrokerAddress => new("Kafka", connectionPool.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        var producer = connectionPool.RentProducer();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var headers = new Headers();

            foreach (var header in message.Headers)
            {
                headers.Add(
                    header.Value != null
                        ? new Header(header.Key, Encoding.UTF8.GetBytes(header.Value))
                        : new Header(header.Key, null)
                );
            }

            var result = await producer
                .ProduceAsync(
                    message.GetName(),
                    new Message<string, byte[]>
                    {
                        Headers = headers,
                        Key =
                            message.Headers.TryGetValue(KafkaHeaders.KafkaKey, out var kafkaMessageKey)
                            && !string.IsNullOrEmpty(kafkaMessageKey)
                                ? kafkaMessageKey
                                : message.GetId(),
                        Value = message.Body.ToArray(),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (result.Status is PersistenceStatus.Persisted or PersistenceStatus.PossiblyPersisted)
            {
                _logger.LogDebug("kafka topic message [{GetName}] has been published.", message.GetName());

                return OperateResult.Success;
            }

            throw new PublisherSentFailedException("kafka message persisted failed!");
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
