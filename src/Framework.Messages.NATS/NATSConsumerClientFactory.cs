// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Exceptions;
using Framework.Messages.Transport;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal sealed class NatsConsumerClientFactory(IOptions<NatsOptions> natsOptions, IServiceProvider serviceProvider)
    : IConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        try
        {
            var client = new NatsConsumerClient(groupName, groupConcurrent, natsOptions, serviceProvider);
            client.Connect();
            return Task.FromResult<IConsumerClient>(client);
        }
        catch (System.Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
