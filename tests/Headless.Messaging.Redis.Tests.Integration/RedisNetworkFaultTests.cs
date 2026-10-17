// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using StackExchange.Redis;
using Tests.Toxiproxy;

namespace Tests;

[Collection<RedisMessagingFixture>]
public sealed class RedisNetworkFaultTests(RedisMessagingFixture fixture) : NetworkFaultTestsBase
{
    protected override string ProviderName => "Redis";

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
                "redis",
                fixture.Container.GetMappedPublicPort(6379),
                cancellationToken
            );
            var destination = $"conformance-{Guid.NewGuid():N}";
            var session = await fixture.CreateSessionAsync(
                MessageLane.Queue,
                destination,
                destination,
                cancellationToken,
                configure: options =>
                    options.Configuration = ConfigurationOptions.Parse($"{proxy.Host}:{proxy.Port},abortConnect=false")
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
        var destination = $"conformance-{Guid.NewGuid():N}";
        return fixture.CreateSessionAsync(MessageLane.Queue, destination, destination, cancellationToken);
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
