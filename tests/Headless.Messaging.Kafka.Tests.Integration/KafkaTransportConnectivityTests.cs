// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Testcontainers.Kafka;

namespace Tests;

[Collection("KafkaConnectivity")]
public sealed class KafkaTransportConnectivityTests : TransportConnectivityTestsBase
{
    private readonly FixedPortKafkaFixture _broker = new();

    // Kafka carries only the Queue lane.
    protected override MessageLane ProbeLane => MessageLane.Queue;

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
        setup.UseKafka(_broker.ConnectionString);
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

[CollectionDefinition("KafkaConnectivity", DisableParallelization = true)]
public sealed class KafkaConnectivityCollection;

/// <summary>A Kafka of its own on a fixed host port, so a stop and start brings it back where the host connects.</summary>
internal sealed class FixedPortKafkaFixture : HeadlessKafkaFixture
{
    private readonly int _hostPort = BrokerHostPort.Reserve();

    public ValueTask StartAsync()
    {
        return InitializeAsync();
    }

    protected override KafkaBuilder Configure()
    {
        // KRaft keeps the broker's registration in its own log; with the embedded ZooKeeper a restart finds the previous
        // session's broker node still registered and the broker refuses to start.
        return new KafkaBuilder(TestImages.Kafka).WithKRaft().WithPortBinding(_hostPort, KafkaBuilder.KafkaPort);
    }
}
