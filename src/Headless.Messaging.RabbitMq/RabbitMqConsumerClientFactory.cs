// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.RabbitMq;

internal sealed class RabbitMqConsumerClientFactory(
    IOptions<RabbitMqOptions> rabbitMqOptions,
    IConnectionChannelPool channelPool,
    IServiceProvider serviceProvider
) : IIntentAwareConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        return CreateAsync(groupName, groupConcurrent, IntentType.Bus);
    }

    public async Task<IConsumerClient> CreateAsync(
        string groupName,
        byte groupConcurrent,
        IntentType intentType
    )
    {
        try
        {
            var client = new RabbitMqConsumerClient(
                groupName,
                groupConcurrent,
                channelPool,
                rabbitMqOptions,
                serviceProvider,
                intentType
            );

            await client.ConnectAsync().ConfigureAwait(false);

            return client;
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
