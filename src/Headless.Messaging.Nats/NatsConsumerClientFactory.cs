// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Nats;

internal sealed class NatsConsumerClientFactory(
    IOptions<NatsMessagingOptions> natsOptions,
    IServiceProvider serviceProvider
) : IIntentAwareConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        return CreateAsync(groupName, groupConcurrent, IntentType.Bus);
    }

    public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent, IntentType intentType)
    {
        var client = new NatsConsumerClient(
            groupName,
            groupConcurrent,
            natsOptions,
            serviceProvider,
            intentType: intentType
        );
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
