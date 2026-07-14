// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.RabbitMq;

internal sealed class RabbitMqConsumerClientFactory(
    IOptions<RabbitMqMessagingOptions> rabbitMqOptions,
    IConnectionChannelPool channelPool,
    IServiceProvider serviceProvider,
    IConsumerRegistry? consumerRegistry = null
) : IIntentAwareConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(
        string groupName,
        byte groupConcurrent,
        CancellationToken cancellationToken = default
    )
    {
        return CreateAsync(groupName, groupConcurrent, IntentType.Bus, cancellationToken);
    }

    public async Task<IConsumerClient> CreateAsync(
        string groupName,
        byte groupConcurrent,
        IntentType intentType,
        CancellationToken cancellationToken = default
    )
    {
        // Resolve outside the broker try/catch so config errors surface as InvalidOperationException,
        // not as a BrokerConnectionException.
        var config = consumerRegistry?.ResolveConsumerConfig<RabbitMqConsumerConfig>(groupName, intentType);

        try
        {
            var client = new RabbitMqConsumerClient(
                groupName,
                groupConcurrent,
                channelPool,
                rabbitMqOptions,
                serviceProvider,
                config,
                intentType
            );

            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            return client;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
