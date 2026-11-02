// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Pulsar;

internal sealed class PulsarConsumerClientFactory : IConsumerClientFactory
{
    private readonly IConnectionFactory _connection;
    private readonly IOptions<PulsarMessagingOptions> _pulsarOptions;
    private readonly IConsumerRegistry? _consumerRegistry;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger? _clientLogger;

    public PulsarConsumerClientFactory(
        IConnectionFactory connection,
        ILoggerFactory loggerFactory,
        IOptions<PulsarMessagingOptions> pulsarOptions,
        IConsumerRegistry? consumerRegistry = null,
        IServiceProvider? serviceProvider = null
    )
    {
        _serviceProvider = serviceProvider;
        _connection = connection;
        _pulsarOptions = pulsarOptions;
        _consumerRegistry = consumerRegistry;

        _clientLogger = _pulsarOptions.Value.EnableClientLog
            ? loggerFactory.CreateLogger<PulsarConsumerClient>()
            : null;
    }

    public Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        // Resolve outside the broker try/catch so config errors surface as InvalidOperationException,
        // not as a BrokerConnectionException.
        var config = _consumerRegistry?.ResolveConsumerConfig<PulsarConsumerConfig>(
            request.SubscriptionName,
            request.Lane
        );

        if (config is { KeyShared: true } && request.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            throw new InvalidOperationException(
                $"Consumer '{request.SubscriptionName}' is an every-instance consumer, whose Pulsar subscription is "
                    + "exclusive to one process, so it cannot use a Key_Shared subscription. Remove KeyShared() from "
                    + "its UsePulsar tuning."
            );
        }

        // An already-cancelled caller must never reach the broker.
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Creating the client touches no broker object: the subscription, every-instance or competing, is opened by
            // SubscribeAsync, so the topology-only client the core creates and disposes leaves nothing behind. The
            // shared Pulsar client is rented here so a disposed or misconfigured connection fails the creation.
            _ = _connection.RentClient();

            return Task.FromResult<IConsumerClient>(
                new PulsarConsumerClient(
                    _pulsarOptions,
                    _connection,
                    request,
                    config,
                    serviceProvider: _serviceProvider,
                    clientLogger: _clientLogger
                )
            );
        }
        catch (Exception e)
        {
            throw BrokerConnectGuard.ConnectFailure(e, cancellationToken);
        }
    }
}
