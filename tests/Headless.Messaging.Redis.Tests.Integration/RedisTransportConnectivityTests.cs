// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Testcontainers.Redis;

namespace Tests;

[Collection("RedisConnectivity")]
public sealed class RedisTransportConnectivityTests : TransportConnectivityTestsBase
{
    private readonly FixedPortRedisFixture _broker = new();

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
        setup.UseRedis(_broker.ConnectionString);
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

[CollectionDefinition("RedisConnectivity", DisableParallelization = true)]
public sealed class RedisConnectivityCollection;

/// <summary>A Redis of its own on a fixed host port, so a stop and start brings it back where the host connects.</summary>
internal sealed class FixedPortRedisFixture : HeadlessRedisFixture
{
    private readonly int _hostPort = BrokerHostPort.Reserve();

    public string ConnectionString => Container.GetConnectionString();

    public ValueTask StartAsync()
    {
        return InitializeAsync();
    }

    protected override RedisBuilder Configure()
    {
        return new RedisBuilder(TestImages.Redis).WithPortBinding(_hostPort, RedisBuilder.RedisPort);
    }
}
