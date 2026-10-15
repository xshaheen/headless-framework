// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using DotNet.Testcontainers.Builders;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Testcontainers.Nats;

namespace Tests;

[Collection("NatsConnectivity")]
public sealed class NatsTransportConnectivityTests : TransportConnectivityTestsBase
{
    private readonly FixedPortNatsFixture _broker = new();

    [Fact]
    public override Task should_report_the_broker_unreachable_while_it_is_down_and_reachable_once_it_is_back()
    {
        return base.should_report_the_broker_unreachable_while_it_is_down_and_reachable_once_it_is_back();
    }

    protected override ValueTask StartBrokerAsync(CancellationToken cancellationToken)
    {
        return _broker.StartAsync();
    }

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseNats(_broker.Container.GetConnectionString());
    }

    protected override Task InterruptBrokerAsync(CancellationToken cancellationToken)
    {
        return _broker.Container.StopAsync(cancellationToken);
    }

    protected override Task RestoreBrokerAsync(CancellationToken cancellationToken)
    {
        return _broker.Container.StartAsync(cancellationToken);
    }

    protected override ValueTask StopBrokerAsync()
    {
        return ((IAsyncDisposable)_broker).DisposeAsync();
    }
}

[CollectionDefinition("NatsConnectivity", DisableParallelization = true)]
public sealed class NatsConnectivityCollection;

/// <summary>
/// A JetStream-enabled NATS of its own on a fixed host port, so a stop and start brings it back where the host connects.
/// </summary>
internal sealed class FixedPortNatsFixture : HeadlessNatsFixture
{
    private static readonly byte[] _Config = Encoding.UTF8.GetBytes(
        """
        port: 4222
        monitor_port: 8222
        jetstream {}
        """
    );

    private readonly int _hostPort = BrokerHostPort.Reserve();

    public ValueTask StartAsync()
    {
        return InitializeAsync();
    }

    protected override NatsBuilder Configure()
    {
        return new NatsBuilder(TestImages.Nats)
            .WithResourceMapping(_Config, "/etc/nats/nats-server.conf")
            .WithPortBinding(_hostPort, NatsBuilder.NatsClientPort)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilMessageIsLogged("Server is ready")
                    .UntilExternalTcpPortIsAvailable(NatsBuilder.NatsClientPort)
            );
    }
}
