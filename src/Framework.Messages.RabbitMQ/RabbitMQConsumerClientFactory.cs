// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Exceptions;
using Framework.Messages.Transport;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal sealed class RabbitMqConsumerClientFactory : IConsumerClientFactory
{
    private readonly IOptions<RabbitMQOptions> _rabbitMqOptions;
    private readonly IConnectionChannelPool _channelPool;
    private readonly IServiceProvider _serviceProvider;

    public RabbitMqConsumerClientFactory(
        IOptions<RabbitMQOptions> rabbitMqOptions,
        IConnectionChannelPool channelPool,
        IServiceProvider serviceProvider)
    {
        _rabbitMqOptions = rabbitMqOptions;
        _channelPool = channelPool;
        _serviceProvider = serviceProvider;
    }

    public async Task<IConsumerClient> CreateAsync(string groupId, byte concurrent)
    {
        try
        {
            var client = new RabbitMqConsumerClient(groupId, concurrent, _channelPool, _rabbitMqOptions, _serviceProvider);

            await client.ConnectAsync().AnyContext();

            return client;
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
