// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.RabbitMq;

internal sealed class RabbitMqConsumerClientFactory(
    IOptions<RabbitMqOptions> rabbitMqOptions,
    IConnectionChannelPool channelPool,
    IServiceProvider serviceProvider
) : IConsumerClientFactory
{
    public async Task<IConsumerClient> CreateAsync(string groupId, byte concurrent)
    {
        try
        {
            var client = new RabbitMqConsumerClient(groupId, concurrent, channelPool, rabbitMqOptions, serviceProvider);

            await client.ConnectAsync().ConfigureAwait(false);

            return client;
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
