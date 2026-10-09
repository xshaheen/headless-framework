// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

[UsedImplicitly]
public sealed class PulsarFixture : HeadlessPulsarFixture
{
    /// <summary>Gets the base address of the broker's HTTP admin API.</summary>
    public Uri AdminUri => new(Container.GetServiceAddress());

    public ValueTask<TransportConsumerConformanceSession> CreateQueueSessionAsync(
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool createReplacement = true
    )
    {
        return CreateSessionAsync(
            ConnectionString,
            MessageLane.Queue,
            cancellationToken,
            destination,
            group,
            createReplacement,
            failEnvelopeBuild: false
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateBusSessionAsync(
        string group,
        CancellationToken cancellationToken,
        string? destination = null
    )
    {
        return CreateSessionAsync(
            ConnectionString,
            MessageLane.Bus,
            cancellationToken,
            destination,
            group,
            failEnvelopeBuild: false
        );
    }

    /// <summary>Opens a session for a conformance endpoint, with the consumer client request the endpoint describes.</summary>
    public ValueTask<TransportConsumerConformanceSession> CreateEndpointSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        return CreateSessionAsync(
            ConnectionString,
            endpoint.Lane,
            cancellationToken,
            endpoint.LogicalName,
            endpoint.SubscriptionName,
            failEnvelopeBuild: false,
            request: endpoint.ToRequest(concurrency: 2)
        );
    }

    /// <summary>Opens a Queue session whose consumer subscribes with the tuned Pulsar consumer options.</summary>
    internal ValueTask<TransportConsumerConformanceSession> CreateTunedQueueSessionAsync(
        string destination,
        PulsarConsumerConfig consumerConfig,
        CancellationToken cancellationToken
    )
    {
        return CreateSessionAsync(
            ConnectionString,
            MessageLane.Queue,
            cancellationToken,
            destination,
            createReplacement: false,
            consumerConfig: consumerConfig
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateMalformedSessionAsync(
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        return CreateSessionAsync(
            ConnectionString,
            MessageLane.Queue,
            cancellationToken,
            destination,
            group,
            failEnvelopeBuild: true
        );
    }

    internal static async ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        string connectionString,
        MessageLane lane,
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool createReplacement = true,
        bool failEnvelopeBuild = false,
        ConsumerClientRequest? request = null,
        Action<PulsarMessagingOptions>? configureOptions = null,
        PulsarConsumerConfig? consumerConfig = null
    )
    {
        destination ??= $"persistent://public/default/conf-{Guid.NewGuid():N}";
        group ??= $"group-{Guid.NewGuid():N}";
        request ??= new ConsumerClientRequest(group, 2, lane);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UsePulsar(options =>
            {
                options.ServiceUrl = connectionString;
                options.NegativeAckRedeliveryDelay = TimeSpan.FromSeconds(1);
                configureOptions?.Invoke(options);
            })
        );
        var serviceProvider = services.BuildServiceProvider();

        try
        {
            var producer =
                lane == MessageLane.Bus
                    ? (ITransport)serviceProvider.GetRequiredService<IBusTransport>()
                    : serviceProvider.GetRequiredService<IQueueTransport>();
            var connectionFactory = serviceProvider.GetRequiredService<IConnectionFactory>();
            var client = await connectionFactory.RentClientAsync(cancellationToken);
            var options = serviceProvider.GetRequiredService<IOptions<PulsarMessagingOptions>>();
            Func<IReadOnlyDictionary<string, string?>, byte[], TransportMessage>? transportMessageFactory = null;
            if (failEnvelopeBuild)
            {
                transportMessageFactory = static (headers, body) =>
                {
                    var malformedHeaders = headers
                        .Where(x => !string.Equals(x.Key, Headers.MessageId, StringComparison.Ordinal))
                        .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                    return new TransportMessage(malformedHeaders, body);
                };
            }
#pragma warning disable CA2000 // Ownership transfers to the returned conformance session or the catch cleanup path.
            var consumer = new PulsarConsumerClient(
                options,
                client,
                request,
                consumerConfig,
                transportMessageFactory: transportMessageFactory
            );
#pragma warning restore CA2000
            consumer.AttachCallbacks(onMessage: null, onLog: _ => { });

            try
            {
                await consumer.SubscribeAsync([destination], cancellationToken);

                return new TransportConsumerConformanceSession(
                    destination,
                    producer,
                    consumer,
                    TimeSpan.FromSeconds(3),
                    serviceProvider.DisposeAsync,
                    createReplacementSession: createReplacement
                        ? replacementToken =>
                            CreateSessionAsync(
                                connectionString,
                                lane,
                                replacementToken,
                                destination,
                                group,
                                createReplacement: false,
                                failEnvelopeBuild,
                                request,
                                configureOptions,
                                consumerConfig
                            )
                        : null
                );
            }
            catch
            {
                await consumer.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await serviceProvider.DisposeAsync();
            throw;
        }
    }
}

[CollectionDefinition("Pulsar", DisableParallelization = true)]
public sealed class PulsarCollection : ICollectionFixture<PulsarFixture>;
