// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using StackExchange.Redis;

namespace Tests;

[Collection<RedisMessagingFixture>]
public sealed class RedisBrokerFaultTests(RedisMessagingFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "Redis";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        var destination = $"conformance-{Guid.NewGuid():N}";
        return fixture.CreateSessionAsync(MessageLane.Queue, destination, destination, cancellationToken);
    }

    protected override async ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        // A server of its own on a fixed host port: a restart must not disturb the collection's server, and the
        // session must find the restarted server at the address it already holds.
#pragma warning disable CA2000 // Ownership moves to the returned session, or the catch disposes it.
        var broker = new FixedPortRedisFixture();
#pragma warning restore CA2000

        try
        {
            await broker.StartAsync();
            var destination = $"conformance-{Guid.NewGuid():N}";
            var session = await fixture.CreateSessionAsync(
                MessageLane.Queue,
                destination,
                destination,
                cancellationToken,
                // The stream lives on the restarted server, which is discarded with it.
                ownsStream: false,
                configure: options => options.Configuration = ConfigurationOptions.Parse(broker.ConnectionString)
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
