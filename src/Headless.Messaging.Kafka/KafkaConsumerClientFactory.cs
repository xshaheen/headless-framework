// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Kafka;

/// <summary>
/// Creates Kafka consumer clients for queue-lane message consumption.
/// </summary>
/// <remarks>
/// Bus (fan-out) consumer creation is not supported by Kafka; attempting to create a bus consumer
/// throws <see cref="NotSupportedException"/>.
/// </remarks>
internal sealed class KafkaConsumerClientFactory(
    IOptions<KafkaMessagingOptions> kafkaOptions,
    IServiceProvider serviceProvider,
    IConsumerRegistry? consumerRegistry = null
) : IConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        if (request.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            throw new NotSupportedException("The Kafka transport does not support every-instance subscriptions.");
        }

        var subscriptionName = request.SubscriptionName;
        var concurrency = request.Concurrency;
        var lane = request.Lane;

        cancellationToken.ThrowIfCancellationRequested();

        if (lane == MessageLane.Bus)
        {
            throw new NotSupportedException(
                "Headless.Messaging.Kafka is a queue transport provider and cannot create bus consumers."
            );
        }

        // Resolve outside the broker try/catch so config errors surface as InvalidOperationException,
        // not as a BrokerConnectionException.
        var config = consumerRegistry?.ResolveConsumerConfig<KafkaConsumerConfig>(subscriptionName, lane);

        try
        {
            return Task.FromResult<IConsumerClient>(
                new KafkaConsumerClient(subscriptionName, concurrency, kafkaOptions, serviceProvider, config)
            );
        }
        catch (Exception e)
        {
            throw BrokerConnectGuard.ConnectFailure(e, cancellationToken);
        }
    }
}
