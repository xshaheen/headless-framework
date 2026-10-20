// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.RabbitMq;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqBrokerFaultTests(RabbitMqFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "RabbitMQ";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    protected override async ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        // A broker of its own on a fixed host port: a restart must not disturb the collection's broker, and the
        // session must find the restarted broker at the address it already holds.
#pragma warning disable CA2000 // Ownership moves to the returned session, or the catch disposes it.
        var broker = new FixedPortRabbitMqFixture();
#pragma warning restore CA2000

        try
        {
            await broker.StartAsync();
            var session = await RabbitMqFixture.CreateConformanceSessionAsync(
                broker.Container.Hostname,
                broker.Container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort),
                cancellationToken
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
