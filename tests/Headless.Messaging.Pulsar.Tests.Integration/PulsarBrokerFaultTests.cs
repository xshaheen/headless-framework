// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

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

    protected override async ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        // A broker of its own on fixed host ports: a restart must not disturb the collection's broker, and the session
        // must find the restarted broker at the address it already holds.
#pragma warning disable CA2000 // Ownership moves to the returned session, or the catch disposes it.
        var broker = new FixedPortPulsarFixture();
#pragma warning restore CA2000

        try
        {
            await broker.StartAsync();
            var session = await PulsarFixture.CreateSessionAsync(
                broker.ConnectionString,
                MessageLane.Queue,
                cancellationToken,
                createReplacement: false
            );

            return new InterruptibleTransportSession(
                session,
                async token =>
                {
                    await broker.Container.StopAsync(token);
                    await broker.Container.StartAsync(token);
                },
                () => ((IAsyncDisposable)broker).DisposeAsync()
            );
        }
        catch
        {
            await ((IAsyncDisposable)broker).DisposeAsync();
            throw;
        }
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause()
    {
        return base.should_resume_delivery_once_after_consumer_pause();
    }

    [Fact]
    public override Task should_resume_delivery_once_after_broker_restart()
    {
        return base.should_resume_delivery_once_after_broker_restart();
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
            MessageLane.Queue,
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
        return InitializeAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Container.StopAsync(cancellationToken);
    }
}
