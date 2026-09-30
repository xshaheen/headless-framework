// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;

namespace Headless.Messaging.Pulsar;

internal sealed class PulsarConsumerClientFactory : IConsumerClientFactory
{
    private readonly IConnectionFactory _connection;
    private readonly IOptions<PulsarMessagingOptions> _pulsarOptions;

    public PulsarConsumerClientFactory(
        IConnectionFactory connection,
        ILoggerFactory loggerFactory,
        IOptions<PulsarMessagingOptions> pulsarOptions
    )
    {
        _connection = connection;
        _pulsarOptions = pulsarOptions;

        if (_pulsarOptions.Value.EnableClientLog)
        {
            PulsarClient.Logger = loggerFactory.CreateLogger<PulsarClient>();
        }
    }

    public async Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        if (request.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            throw new System.NotSupportedException(
                "The Pulsar transport does not support every-instance subscriptions."
            );
        }

        var subscriptionName = request.SubscriptionName;
        var concurrency = request.Concurrency;
        var lane = request.Lane;

        try
        {
            var client = await _connection.RentClientAsync(cancellationToken).ConfigureAwait(false);
            var consumerClient = new PulsarConsumerClient(_pulsarOptions, client, subscriptionName, concurrency, lane);
            return consumerClient;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
