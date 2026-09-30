// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.RabbitMq;

internal sealed class RabbitMqConsumerClientFactory(
    IOptions<RabbitMqMessagingOptions> rabbitMqOptions,
    IConnectionChannelPool channelPool,
    IServiceProvider serviceProvider,
    IConsumerRegistry? consumerRegistry = null
) : IConsumerClientFactory
{
    public async Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        if (request.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            throw new NotSupportedException("The RabbitMQ transport does not support every-instance subscriptions.");
        }

        var subscriptionName = request.SubscriptionName;
        var concurrency = request.Concurrency;
        var lane = request.Lane;

        // Resolve outside the broker try/catch so config errors surface as InvalidOperationException,
        // not as a BrokerConnectionException.
        var config = consumerRegistry?.ResolveConsumerConfig<RabbitMqConsumerConfig>(subscriptionName, lane);

        try
        {
            var client = new RabbitMqConsumerClient(
                subscriptionName,
                concurrency,
                channelPool,
                rabbitMqOptions,
                serviceProvider,
                config,
                lane
            );

            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            return client;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
