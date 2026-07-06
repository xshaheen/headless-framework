// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusConsumerClientFactory(
    ILoggerFactory loggerFactory,
    IOptions<AzureServiceBusMessagingOptions> asbOptions,
    IServiceProvider serviceProvider
) : IIntentAwareConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        return CreateAsync(groupName, groupConcurrent, IntentType.Bus);
    }

    public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent, IntentType intentType)
    {
        // Bus groups are Azure subscriptions. Queue groups are framework-local
        // handler selectors; their broker entity names are validated on SubscribeAsync.
        if (intentType == IntentType.Bus)
        {
            AzureServiceBusConsumerClient.CheckValidSubscriptionName(groupName);
        }

        try
        {
            var client = new AzureServiceBusConsumerClient(
                loggerFactory.CreateLogger<AzureServiceBusConsumerClient>(),
                groupName,
                groupConcurrent,
                asbOptions,
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
