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
        var client = new NatsConsumerClient(groupName, groupConcurrent, natsOptions, serviceProvider);
        try
        {
            await client.ConnectAsync().ConfigureAwait(false);
            return client;
        }
        catch (OperationCanceledException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception e)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw new BrokerConnectionException(e);
        }
    }
}
