// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Exceptions;
using Framework.Messages.Transport;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

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

            await client.ConnectAsync();

            return client;
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
