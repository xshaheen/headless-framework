// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Toxiproxy;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqNetworkFaultTests(RabbitMqFixture fixture) : NetworkFaultTestsBase
{
    protected override string ProviderName => "RabbitMQ";

    protected override async ValueTask<NetworkFaultSession> CreateNetworkFaultSessionAsync(
        CancellationToken cancellationToken
    )
    {
#pragma warning disable CA2000 // Ownership moves to the returned session, or the catch disposes it.
        var toxiproxy = new ToxiproxyServer();
#pragma warning restore CA2000

        try
        {
            await toxiproxy.StartAsync(cancellationToken);
            var proxy = await toxiproxy.CreateProxyAsync("rabbitmq", fixture.Port, cancellationToken);
            var session = await RabbitMqFixture.CreateConformanceSessionAsync(
                proxy.Host,
                proxy.Port,
                cancellationToken
            );

            return new NetworkFaultSession(session, proxy, toxiproxy.DisposeAsync);
        }
        catch
        {
            await toxiproxy.DisposeAsync();
            throw;
        }
    }

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_resume_delivery_once_after_the_connection_is_dropped()
    {
        return base.should_resume_delivery_once_after_the_connection_is_dropped();
    }

    [Fact]
    public override Task should_keep_consuming_after_publishes_during_an_outage()
    {
        return base.should_keep_consuming_after_publishes_during_an_outage();
    }
}
