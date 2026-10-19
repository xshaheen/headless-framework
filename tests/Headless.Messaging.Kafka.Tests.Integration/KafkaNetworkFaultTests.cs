// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Toxiproxy;

namespace Tests;

[Collection("Kafka")]
public sealed class KafkaNetworkFaultTests(KafkaFixture fixture) : NetworkFaultTestsBase
{
    private ProxiedKafkaContainer? _broker;

    protected override string ProviderName => "Kafka";

    protected override async ValueTask<NetworkFaultSession> CreateNetworkFaultSessionAsync(
        CancellationToken cancellationToken
    )
    {
        // The collection's broker advertises its own mapped port, so a client would leave the proxy after the
        // bootstrap: this broker advertises the route instead.
#pragma warning disable CA2000 // Ownership moves to the returned session, or the catch disposes it.
        var toxiproxy = new ToxiproxyServer();
#pragma warning restore CA2000
        ProxiedKafkaContainer? broker = null;

        try
        {
            await toxiproxy.StartAsync(cancellationToken);
            broker = await ProxiedKafkaContainer.StartAsync(toxiproxy, cancellationToken);
            var proxiedBootstrap = broker.ProxiedBootstrap;
            var session = await fixture.CreateConformanceSessionAsync(
                cancellationToken,
                createReplacement: false,
                configure: options => options.Servers = proxiedBootstrap
            );
            _broker = broker;

            return new NetworkFaultSession(
                session,
                broker.Proxy,
                async () =>
                {
                    try
                    {
                        await broker.DisposeAsync();
                    }
                    finally
                    {
                        await toxiproxy.DisposeAsync();
                    }
                }
            );
        }
        catch
        {
            if (broker is not null)
            {
                await broker.DisposeAsync();
            }

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
    public Task should_stop_consuming_while_the_proxy_is_cut_and_resume_once_after()
    {
        return AssertTrafficStopsWhileTheLinkIsCutAsync(
            (message, cancellationToken) =>
                (
                    _broker ?? throw new InvalidOperationException("The proxied broker has not started.")
                ).PublishDirectAsync(message, cancellationToken)
        );
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
