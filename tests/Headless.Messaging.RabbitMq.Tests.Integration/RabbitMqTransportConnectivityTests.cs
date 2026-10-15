// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Testcontainers.RabbitMq;

namespace Tests;

[Collection("RabbitMqConnectivity")]
public sealed class RabbitMqTransportConnectivityTests : TransportConnectivityTestsBase
{
    private readonly FixedPortRabbitMqFixture _broker = new();

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
        setup.UseRabbitMq(options =>
        {
            options.HostName = _broker.Container.Hostname;
            options.Port = _broker.Container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);
            options.UserName = RabbitMqBuilder.DefaultUsername;
            options.Password = RabbitMqBuilder.DefaultPassword;
        });
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

[CollectionDefinition("RabbitMqConnectivity", DisableParallelization = true)]
public sealed class RabbitMqConnectivityCollection;

/// <summary>A RabbitMQ of its own on a fixed host port, so a stop and start brings it back where the host connects.</summary>
internal sealed class FixedPortRabbitMqFixture : HeadlessRabbitMqFixture
{
    private readonly int _hostPort = BrokerHostPort.Reserve();

    public ValueTask StartAsync()
    {
        return InitializeAsync();
    }

    protected override RabbitMqBuilder Configure()
    {
        return new RabbitMqBuilder(TestImages.RabbitMq).WithPortBinding(_hostPort, RabbitMqBuilder.RabbitMqPort);
    }
}
