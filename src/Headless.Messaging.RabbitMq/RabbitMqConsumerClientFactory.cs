// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
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

        var subscriptionName = request.SubscriptionName;
        var concurrency = request.Concurrency;
        var lane = request.Lane;

        // Resolve outside the broker try/catch so config errors surface as InvalidOperationException,
        // not as a BrokerConnectionException.
        var config = consumerRegistry?.ResolveConsumerConfig<RabbitMqConsumerConfig>(subscriptionName, lane);

        // An already-cancelled caller must never build a half-connected client: the connect below would only
        // observe the token after it opened a broker connection.
        cancellationToken.ThrowIfCancellationRequested();

        var client = new RabbitMqConsumerClient(
            subscriptionName,
            concurrency,
            channelPool,
            rabbitMqOptions,
            serviceProvider,
            config,
            lane,
            kind: request.Kind
        );

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            return client;
        }
        catch (Exception e)
        {
            // An every-instance client may already own a connection; release it with the failed client.
            await client.DisposeAsync().ConfigureAwait(false);
            throw BrokerConnectGuard.ConnectFailure(e, cancellationToken);
        }
    }
}
