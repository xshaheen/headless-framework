// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Kafka;

/// <summary>
/// Creates Kafka consumer clients for queue-lane message consumption.
/// </summary>
/// <remarks>
/// Bus (fan-out) consumer creation is not supported by Kafka; attempting to create a bus consumer
/// throws <see cref="NotSupportedException"/>.
/// </remarks>
public sealed class KafkaConsumerClientFactory(
    IOptions<MessagingKafkaOptions> kafkaOptions,
    IServiceProvider serviceProvider,
    IConsumerRegistry? consumerRegistry = null
) : IIntentAwareConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        return CreateAsync(groupName, groupConcurrent, IntentType.Queue);
    }

    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent, IntentType intentType)
    {
        if (intentType == IntentType.Bus)
        {
            throw new NotSupportedException(
                "Headless.Messaging.Kafka is a queue transport provider and cannot create bus consumers."
            );
        }

        // Resolve outside the broker try/catch so config errors surface as InvalidOperationException,
        // not as a BrokerConnectionException.
        var config = consumerRegistry?.ResolveConsumerConfig<KafkaConsumerConfig>(groupName, intentType);

        try
        {
            return Task.FromResult<IConsumerClient>(
                new KafkaConsumerClient(groupName, groupConcurrent, kafkaOptions, serviceProvider, config)
            );
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
