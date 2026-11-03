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

/// <summary>
/// A Pulsar of its own behind a <see cref="BrokerPortForwarder"/> on its binary-protocol port, so a stop and start brings
/// it back at the address the host connects to.
/// </summary>
internal sealed class FixedPortPulsarFixture : HeadlessPulsarFixture
{
    private readonly BrokerPortForwarder _forwarder;

    public FixedPortPulsarFixture()
    {
        _forwarder = new BrokerPortForwarder(() => Container, PulsarBuilder.PulsarBrokerDataPort);
    }

    /// <summary>Gets the binary-protocol service URL, which stays the same across a stop and start.</summary>
    public new string ConnectionString => $"pulsar://{_forwarder.Address}";

    public ValueTask StartAsync()
    {
        return InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await base.DisposeAsyncCore();
        }
        finally
        {
            await _forwarder.DisposeAsync();
        }
    }
}
