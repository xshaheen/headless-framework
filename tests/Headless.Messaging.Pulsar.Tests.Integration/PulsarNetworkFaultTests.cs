// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Pulsar;
using Tests.Toxiproxy;

namespace Tests;

[Collection("Pulsar")]
public sealed class PulsarNetworkFaultTests(PulsarFixture fixture) : NetworkFaultTestsBase
{
    protected override string ProviderName => "Pulsar";

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
            var proxy = await toxiproxy.CreateProxyAsync(
                "pulsar",
                fixture.Container.GetMappedPublicPort(PulsarBuilder.PulsarBrokerDataPort),
                cancellationToken
            );
            // Unlike Kafka's, this client keeps every connection on the service URL it was given, so the collection's
            // broker serves the route unchanged; the cut-link test below fails if a connection ever leaves the route.
            var session = await PulsarFixture.CreateSessionAsync(
                $"pulsar://{proxy.Host}:{proxy.Port}",
                MessageLane.Queue,
                cancellationToken,
                createReplacement: false
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
        return fixture.CreateQueueSessionAsync(cancellationToken);
    }

    [Fact]
    public async Task should_stop_consuming_while_the_proxy_is_cut_and_resume_once_after()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UsePulsar(options => options.ServiceUrl = fixture.ConnectionString)
        );
        await using var serviceProvider = services.BuildServiceProvider();
        var directProducer = serviceProvider.GetRequiredService<IQueueTransport>();

        await AssertTrafficStopsWhileTheLinkIsCutAsync(directProducer.SendAsync);
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
