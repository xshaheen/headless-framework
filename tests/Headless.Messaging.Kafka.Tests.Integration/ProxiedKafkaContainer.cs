// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Toxiproxy;

namespace Tests;

/// <summary>
/// A single-node KRaft Kafka whose client listener advertises a Toxiproxy route. A Kafka client connects to the
/// address the broker advertises, not to the address it bootstrapped from, so a broker that advertises its own port
/// would let every connection after the first bypass the proxy. A second listener advertises the broker's own host
/// port, for a producer the test keeps off the route.
/// </summary>
internal sealed class ProxiedKafkaContainer : IAsyncDisposable
{
    private const int _ProxiedPort = 9092;
    private const int _InterBrokerPort = 9093;
    private const int _ControllerPort = 9094;
    private const int _DirectPort = 9095;

    // Any fixed base64 UUID; a KRaft log is formatted once for the cluster it names, and this broker never outlives its test.
    private const string _ClusterId = "4L6g3nShT-eMCtK--X86sw";

    private readonly IContainer _container;
    private readonly KafkaProducerProvider _directProducers;
    private readonly KafkaTransport _directProducer;

    private ProxiedKafkaContainer(IContainer container, ToxiproxyProxy proxy, string directBootstrap)
    {
        _container = container;
        Proxy = proxy;
        DirectBootstrap = directBootstrap;
        _directProducers = new KafkaProducerProvider(
            NullLogger<KafkaProducerProvider>.Instance,
            Options.Create(new KafkaMessagingOptions { Servers = directBootstrap })
        );
        _directProducer = new KafkaTransport(NullLogger<KafkaTransport>.Instance, _directProducers);
    }

    /// <summary>Gets the route every proxied connection takes; cutting it cuts the client off the broker.</summary>
    public ToxiproxyProxy Proxy { get; }

    /// <summary>Gets the bootstrap address of the route, which is also the address the broker advertises on it.</summary>
    public string ProxiedBootstrap => $"{Proxy.Host}:{Proxy.Port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Gets the bootstrap address of the listener that does not go through the route.</summary>
    public string DirectBootstrap { get; }

    /// <summary>
    /// Creates the route on <paramref name="toxiproxy"/>, then starts a broker that advertises it. The route is created
    /// first because the broker must know the proxy's host port before it starts, and Toxiproxy dials its upstream only
    /// when a client connects.
    /// </summary>
    public static async Task<ProxiedKafkaContainer> StartAsync(
        ToxiproxyServer toxiproxy,
        CancellationToken cancellationToken
    )
    {
        var proxiedHostPort = BrokerHostPort.Reserve();
        var directHostPort = BrokerHostPort.Reserve();
        var proxy = await toxiproxy.CreateProxyAsync("kafka", proxiedHostPort, cancellationToken);
        var host = proxy.Host;
        var directBootstrap = $"{host}:{directHostPort.ToString(CultureInfo.InvariantCulture)}";

        var container = new ContainerBuilder(TestImages.Kafka)
            .WithPortBinding(proxiedHostPort, _ProxiedPort)
            .WithPortBinding(directHostPort, _DirectPort)
            .WithEnvironment("CLUSTER_ID", _ClusterId)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", $"1@localhost:{_ControllerPort}")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "BROKER")
            .WithEnvironment(
                "KAFKA_LISTENERS",
                $"PLAINTEXT://0.0.0.0:{_ProxiedPort},BROKER://0.0.0.0:{_InterBrokerPort},"
                    + $"CONTROLLER://0.0.0.0:{_ControllerPort},DIRECT://0.0.0.0:{_DirectPort}"
            )
            .WithEnvironment(
                "KAFKA_ADVERTISED_LISTENERS",
                $"PLAINTEXT://{host}:{proxy.Port.ToString(CultureInfo.InvariantCulture)},"
                    + $"BROKER://localhost:{_InterBrokerPort},DIRECT://{directBootstrap}"
            )
            .WithEnvironment(
                "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                "PLAINTEXT:PLAINTEXT,BROKER:PLAINTEXT,CONTROLLER:PLAINTEXT,DIRECT:PLAINTEXT"
            )
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_NUM_PARTITIONS", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
            .Build();

        try
        {
            await container.StartAsync(cancellationToken);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }

        return new ProxiedKafkaContainer(container, proxy, directBootstrap);
    }

    /// <summary>Publishes over the listener that does not go through the route.</summary>
    public Task<OperateResult> PublishDirectAsync(TransportMessage message, CancellationToken cancellationToken) =>
        _directProducer.SendAsync(message, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _directProducer.DisposeAsync();
            _directProducers.Dispose();
        }
        finally
        {
            await _container.DisposeAsync();
        }
    }
}
