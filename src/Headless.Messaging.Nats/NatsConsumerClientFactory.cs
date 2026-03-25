// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Nats;

internal sealed class NatsConsumerClientFactory(
    IOptions<MessagingNatsOptions> natsOptions,
    IServiceProvider serviceProvider
) : IConsumerClientFactory
{
    public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        try
        {
            var client = new NatsConsumerClient(groupName, groupConcurrent, natsOptions, serviceProvider);
            await client.ConnectAsync().ConfigureAwait(false);
            return client;
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
