// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Kafka;

public sealed class KafkaConsumerClientFactory(
    IOptions<MessagingKafkaOptions> kafkaOptions,
    IServiceProvider serviceProvider
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

        try
        {
            return Task.FromResult<IConsumerClient>(
                new KafkaConsumerClient(groupName, groupConcurrent, kafkaOptions, serviceProvider)
            );
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
