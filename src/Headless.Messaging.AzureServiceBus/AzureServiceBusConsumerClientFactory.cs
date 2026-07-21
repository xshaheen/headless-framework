// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusConsumerClientFactory(
    ILoggerFactory loggerFactory,
    IOptions<AzureServiceBusMessagingOptions> asbOptions,
    IServiceProvider serviceProvider,
    IAzureServiceBusClientPool clientPool
) : IConsumerClientFactory
{
    public async Task<IConsumerClient> CreateAsync(
        string groupName,
        byte groupConcurrent,
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        var intentType = MessageLaneCompatibility.ToIntentType(lane);

        // Bus groups are Azure subscriptions. Queue groups are framework-local
        // handler selectors; their broker entity names are validated on SubscribeAsync.
        if (intentType == IntentType.Bus)
        {
            AzureServiceBusConsumerClient.CheckValidSubscriptionName(groupName);
        }

        AzureServiceBusConsumerClient? client = null;

        try
        {
            client = new AzureServiceBusConsumerClient(
                loggerFactory.CreateLogger<AzureServiceBusConsumerClient>(),
                groupName,
                groupConcurrent,
                asbOptions,
                serviceProvider,
                clientPool,
                intentType
            );

            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            return client;
        }
        catch (OperationCanceledException)
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            throw new BrokerConnectionException(e);
        }
    }
}
