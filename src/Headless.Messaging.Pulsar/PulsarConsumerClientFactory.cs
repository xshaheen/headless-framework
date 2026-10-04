// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
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

        // An already-cancelled caller must never reach the broker: the client rent below would only observe the
        // token after it started building the Pulsar client.
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Creating the client touches no broker object: the subscription, every-instance or competing, is opened by
            // SubscribeAsync, so the topology-only client the core creates and disposes leaves nothing behind.
            var client = await _connection.RentClientAsync(cancellationToken).ConfigureAwait(false);
            return new PulsarConsumerClient(_pulsarOptions, client, request);
        }
        catch (Exception e)
        {
            throw BrokerConnectGuard.ConnectFailure(e, cancellationToken);
        }
    }
}
