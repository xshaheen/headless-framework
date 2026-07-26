// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
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
        string groupName,
        byte groupConcurrent,
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var client = await _connection.RentClientAsync(cancellationToken).ConfigureAwait(false);
            var consumerClient = new PulsarConsumerClient(
                _pulsarOptions,
                client,
                groupName,
                groupConcurrent,
                MessageLaneCompatibility.ToIntentType(lane)
            );
            return consumerClient;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
