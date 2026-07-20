// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

[Collection("Pulsar")]
public sealed class PulsarBrokerFaultTests(PulsarFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "Pulsar";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateQueueSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause()
    {
        return base.should_resume_delivery_once_after_consumer_pause();
    }

    [Fact]
    public async Task should_resume_delivery_once_after_broker_restart()
    {
        RequireSupport(TransportConformanceScenario.BrokerInterruptionRecovery);

        await using var broker = new IsolatedPulsarFixture();
        await broker.StartAsync();
        await using var session = await PulsarFixture.CreateSessionAsync(
            broker.ConnectionString,
            IntentType.Queue,
            AbortToken
        );
        await session.StartAsync(cancellationToken: AbortToken);

        var beforeRestart = CreateFaultProbe(session.Destination);
        var beforeResult = await session.PublishAsync(beforeRestart, AbortToken);
        beforeResult.Succeeded.Should().BeTrue();
        var beforeDelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await session.Consumer.CommitAsync(beforeDelivery.SettlementValue, AbortToken);

        await broker.RestartAsync(AbortToken);

        var afterRestart = CreateFaultProbe(session.Destination);
        var afterResult = await session
            .PublishAsync(afterRestart, AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        afterResult.Succeeded.Should().BeTrue();
        var afterDelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken);
        afterDelivery.Message.Id.Should().Be(afterRestart.Id);
        await session.Consumer.CommitAsync(afterDelivery.SettlementValue, AbortToken);
        (await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken)).Should().BeTrue();
    }
}

[Collection("PulsarOutage")]
public sealed class PulsarOutageTests : TestBase
{
    [Fact]
    public async Task should_bound_shutdown_during_broker_outage()
    {
        await using var broker = new IsolatedPulsarFixture();
        await broker.StartAsync();
        await using var session = await PulsarFixture.CreateSessionAsync(
            broker.ConnectionString,
            IntentType.Queue,
            AbortToken
        );
        await session.StartAsync(cancellationToken: AbortToken);
        await broker.StopAsync(AbortToken);

        var stopwatch = Stopwatch.StartNew();
        await session.StopAsync(TimeSpan.FromMilliseconds(500));

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}

[CollectionDefinition("PulsarOutage", DisableParallelization = true)]
public sealed class PulsarOutageCollection;

internal sealed class IsolatedPulsarFixture : HeadlessPulsarFixture
{
    public ValueTask StartAsync()
    {
        return base.InitializeAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Container.StopAsync(cancellationToken);
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await Container.StopAsync(cancellationToken);
        await Container.StartAsync(cancellationToken);
    }
}
