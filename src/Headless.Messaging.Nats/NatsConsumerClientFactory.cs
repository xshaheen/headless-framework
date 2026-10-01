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

        // An every-instance client needs no name of its own: it opens plain subscriptions on the Bus subjects, which
        // the server ties to this client's connection rather than to a durable consumer.
        var client = new NatsConsumerClient(
            request.SubscriptionName,
            request.Concurrency,
            natsOptions,
            serviceProvider,
            lane: request.Lane,
            kind: request.Kind
        );
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
