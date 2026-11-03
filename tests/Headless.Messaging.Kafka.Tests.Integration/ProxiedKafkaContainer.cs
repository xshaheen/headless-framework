// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using DotNet.Testcontainers.Containers;
using Headless.Messaging;
using Headless.Messaging.Kafka;
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
    private const int _DirectPort = 9095;

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
    /// Starts a broker, then creates the route on <paramref name="toxiproxy"/> to the host port Docker published for it,
    /// and only then lets the broker come up advertising the route. Toxiproxy dials its upstream only when a client
    /// connects, and the broker reads its advertised addresses only once they are written, so no host port is chosen
    /// before Docker binds it.
    /// </summary>
    public static async Task<ProxiedKafkaContainer> StartAsync(
        ToxiproxyServer toxiproxy,
        CancellationToken cancellationToken
    )
    {
        ToxiproxyProxy? proxy = null;
        string? directBootstrap = null;

        var container = SingleNodeKafka
            .CreateBuilder(
                [("PLAINTEXT", _ProxiedPort), ("DIRECT", _DirectPort)],
                async (started, token) =>
                {
                    proxy = await toxiproxy.CreateProxyAsync("kafka", started.GetMappedPublicPort(_ProxiedPort), token);
                    directBootstrap =
                        $"{started.Hostname}:{started.GetMappedPublicPort(_DirectPort).ToString(CultureInfo.InvariantCulture)}";

                    return $"PLAINTEXT://{proxy.Host}:{proxy.Port.ToString(CultureInfo.InvariantCulture)},"
                        + $"DIRECT://{directBootstrap}";
                }
            )
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

        // The startup callback set both before the broker's wait strategy let StartAsync return.
        return new ProxiedKafkaContainer(container, proxy!, directBootstrap!);
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
