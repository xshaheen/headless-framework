// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Testcontainers.Pulsar;

namespace Tests;

[Collection("PulsarConnectivity")]
public sealed class PulsarTransportConnectivityTests : TransportConnectivityTestsBase
{
    private readonly FixedPortPulsarFixture _broker = new();

    // A standalone Pulsar takes the longest of the brokers to come back up.
    protected override TimeSpan RecoveryTimeout => TimeSpan.FromMinutes(3);

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
        setup.UsePulsar(_broker.ConnectionString);
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

[CollectionDefinition("PulsarConnectivity", DisableParallelization = true)]
public sealed class PulsarConnectivityCollection;

/// <summary>A Pulsar of its own on fixed host ports, so a stop and start brings it back where the host connects.</summary>
internal sealed class FixedPortPulsarFixture : HeadlessPulsarFixture
{
    private readonly int _brokerPort = BrokerHostPort.Reserve();
    private readonly int _webPort = BrokerHostPort.Reserve();

    public ValueTask StartAsync()
    {
        return InitializeAsync();
    }

    protected override PulsarBuilder Configure()
    {
        return new PulsarBuilder(TestImages.Pulsar)
            .WithPortBinding(_brokerPort, PulsarBuilder.PulsarBrokerDataPort)
            .WithPortBinding(_webPort, PulsarBuilder.PulsarWebServicePort);
    }
}
