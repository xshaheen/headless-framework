// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Headless.Messaging.Aws;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.LocalStack;
using Tests.Toxiproxy;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class AmazonSqsNetworkFaultTests(LocalStackTestFixture fixture) : NetworkFaultTestsBase
{
    protected override string ProviderName => "AWS/LocalStack";

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
                "localstack",
                fixture.Container.GetMappedPublicPort(LocalStackBuilder.LocalStackPort),
                cancellationToken
            );
            var session = await LocalStackTestFixture.CreateConformanceSessionAsync(
                $"http://{proxy.Host}:{proxy.Port}",
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
    public async Task should_stop_consuming_while_the_proxy_is_cut_and_resume_once_after()
    {
        await using var directProducer = new AmazonSqsQueueTransport(
            NullLogger<AmazonSqsQueueTransport>.Instance,
            Options.Create(
                new AmazonSqsMessagingOptions
                {
                    Region = Amazon.RegionEndpoint.USEast1,
                    SnsServiceUrl = fixture.ConnectionString,
                    SqsServiceUrl = fixture.ConnectionString,
                    Credentials = new BasicAWSCredentials("test", "test"),
                }
            )
        );

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
