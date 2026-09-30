// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Nats;

internal sealed class NatsConsumerClientFactory(
    IOptions<NatsMessagingOptions> natsOptions,
    IServiceProvider serviceProvider
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
            throw new NotSupportedException("The NATS transport does not support every-instance subscriptions.");
        }

        var subscriptionName = request.SubscriptionName;
        var concurrency = request.Concurrency;
        var lane = request.Lane;

        var client = new NatsConsumerClient(subscriptionName, concurrency, natsOptions, serviceProvider, lane: lane);
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
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
