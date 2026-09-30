// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
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
    // Azure Resource Manager naming rules for Service Bus subscriptions: 1-50 letters, digits, '.', '-', or '_',
    // starting and ending with a letter or digit.
    private static readonly BusNameRules _SubscriptionRules = new(
        maxLength: 50,
        isAllowed: static c => c is '.' or '-' or '_',
        alphanumericBoundaries: true
    );

    /// <summary>Returns the topic subscription a Bus consumer identity reads through.</summary>
    internal static string BusSubscriptionName(string identity) => BusNameBuilder.Build(identity, _SubscriptionRules);

    public async Task<IConsumerClient> CreateAsync(
        string subscriptionName,
        byte concurrency,
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        // A Bus consumer identity becomes an Azure subscription name. Queue subscription names are framework-local
        // handler selectors; their broker entity names are validated on SubscribeAsync.
        if (lane == MessageLane.Bus)
        {
            subscriptionName = BusSubscriptionName(subscriptionName);
            AzureServiceBusConsumerClient.CheckValidSubscriptionName(subscriptionName);
        }

        AzureServiceBusConsumerClient? client = null;

        try
        {
            client = new AzureServiceBusConsumerClient(
                loggerFactory.CreateLogger<AzureServiceBusConsumerClient>(),
                subscriptionName,
                concurrency,
                asbOptions,
                serviceProvider,
                clientPool,
                lane
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
