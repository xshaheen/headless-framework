// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Kafka;

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

        try
        {
            return Task.FromResult<IConsumerClient>(
                new KafkaConsumerClient(
                    groupName,
                    groupConcurrent,
                    kafkaOptions,
                    serviceProvider,
                    _ResolveConsumerConfig<KafkaConsumerConfig>(groupName, intentType)
                )
            );
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }

    private TConfig? _ResolveConsumerConfig<TConfig>(string groupName, IntentType intentType)
        where TConfig : class
    {
        if (consumerRegistry is null)
        {
            return null;
        }

        var configs = consumerRegistry
            .GetAll()
            .Where(consumer =>
                consumer.IntentType == intentType && string.Equals(consumer.Group, groupName, StringComparison.Ordinal)
            )
            .Select(consumer =>
                consumer.ProviderConfigs.TryGetValue(typeof(TConfig), out var config) ? config as TConfig : null
            )
            .Where(static config => config is not null)
            .Distinct()
            .ToArray();

        return configs.Length switch
        {
            0 => null,
            1 => configs[0],
            _ => throw new InvalidOperationException(
                $"Consumer group '{groupName}' has conflicting {typeof(TConfig).Name} provider configs."
            ),
        };
    }
}
