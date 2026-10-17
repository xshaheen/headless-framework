// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection("Kafka")]
public sealed class KafkaBrokerFaultTests(KafkaFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "Kafka";

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
        var broker = new FixedPortKafkaFixture();

        try
        {
            await broker.StartAsync();
            var session = await fixture.CreateConformanceSessionAsync(
                cancellationToken,
                createReplacement: false,
                configure: options => options.Servers = broker.ConnectionString
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
