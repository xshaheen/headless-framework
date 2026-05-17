// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusConsumerClientFactory(
    ILoggerFactory loggerFactory,
    IOptions<AzureServiceBusOptions> asbOptions,
    IServiceProvider serviceProvider
) : IConsumerClientFactory
{
    public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        // Validate at the boundary so an invalid group name surfaces as a clear ArgumentException
        // at registration time instead of a wrapped Azure SDK failure deep inside ConnectAsync.
        AzureServiceBusConsumerClient.CheckValidSubscriptionName(groupName);

        try
        {
            var client = new AzureServiceBusConsumerClient(
                loggerFactory.CreateLogger<AzureServiceBusConsumerClient>(),
                groupName,
                groupConcurrent,
                asbOptions,
                serviceProvider
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
