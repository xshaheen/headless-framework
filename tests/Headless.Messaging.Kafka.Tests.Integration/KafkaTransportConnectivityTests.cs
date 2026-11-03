// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Containers;
using Headless.Messaging;

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
        return new ValueTask(_broker.StartAsync(cancellationToken));
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
        return _broker.DisposeAsync();
    }
}

[CollectionDefinition("KafkaConnectivity", DisableParallelization = true)]
public sealed class KafkaConnectivityCollection;

/// <summary>
/// A Kafka of its own behind a <see cref="BrokerPortForwarder"/> that it also advertises, so a stop and start brings
/// it back at the address the host connects to. The broker's advertised address must be the forwarder's: a client
/// that bootstrapped through the forwarder connects next to whatever address the broker advertises.
/// </summary>
internal sealed class FixedPortKafkaFixture : IAsyncDisposable
{
    private const int _ClientPort = 9092;

    private readonly BrokerPortForwarder _forwarder;

    public FixedPortKafkaFixture()
    {
        _forwarder = new BrokerPortForwarder(_GetContainer, _ClientPort);
        var advertised = $"PLAINTEXT://{_forwarder.Address}";
        Container = SingleNodeKafka
            .CreateBuilder([("PLAINTEXT", _ClientPort)], (_, _) => Task.FromResult(advertised))
            .Build();
    }

    public IContainer Container { get; }

    /// <summary>Gets the bootstrap address, which stays the same across a stop and start.</summary>
    public string ConnectionString => _forwarder.Address;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Container.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Container.DisposeAsync();
        }
        finally
        {
            await _forwarder.DisposeAsync();
        }
    }

    private IContainer _GetContainer()
    {
        return Container;
    }
}
