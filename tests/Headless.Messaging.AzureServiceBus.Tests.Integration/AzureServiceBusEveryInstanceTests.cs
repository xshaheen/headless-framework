// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests;

[Collection("AzureServiceBus")]
public sealed class AzureServiceBusEveryInstanceTests(AzureServiceBusFixture fixture) : TestBase
{
    [Fact]
    public Task should_deliver_every_bus_message_to_every_every_instance_replica()
    {
        return TransportProviderConformance.AssertBusEveryInstanceAsync(
            new AzureServiceBusProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public async Task should_fan_out_one_bus_message_to_two_every_instance_sessions()
    {
        // given
        var topic = await fixture.CreateTopicAsync(AbortToken);
        var logicalName = $"every-{Guid.NewGuid():N}";
        await using var first = await fixture.CreateConformanceSessionAsync(
            _Endpoint(logicalName),
            topic,
            ownsEntity: false,
            AbortToken
        );
        await using var second = await fixture.CreateConformanceSessionAsync(
            _Endpoint(logicalName),
            topic,
            ownsEntity: false,
            AbortToken
        );

        // when / then
        await TransportBusConformance.AssertEveryInstanceFanOutAsync(first, second, AbortToken);
    }

    [Fact]
    public async Task should_create_an_idle_deleting_subscription_and_delete_it_on_graceful_dispose()
    {
        // given
        var topic = await fixture.CreateTopicAsync(AbortToken);
        var endpoint = _Endpoint($"every-{Guid.NewGuid():N}");
        var subscription = AzureServiceBusConsumerClientFactory.BusSubscriptionName(endpoint.ToRequest());
        var session = await fixture.CreateConformanceSessionAsync(endpoint, topic, ownsEntity: false, AbortToken);

        try
        {
            await session.StartAsync(cancellationToken: AbortToken);

            // then: a crashed process leaves the subscription for at most the idle period
            var live = await fixture.GetSubscriptionOrDefaultAsync(topic, subscription, AbortToken);
            live.Should().NotBeNull();
            live!.AutoDeleteOnIdle.Should().Be(TimeSpan.FromMinutes(5));
        }
        finally
        {
            // when: disposing the session disposes its service provider, as a host does on a graceful stop
            await session.DisposeAsync();
        }

        // then
        (await fixture.GetSubscriptionOrDefaultAsync(topic, subscription, AbortToken))
            .Should()
            .BeNull();
    }

    private static TransportConformanceEndpoint _Endpoint(string logicalName) =>
        new(MessageLane.Bus, logicalName, "conformance.every-instance", "replica")
        {
            Kind = ConsumerSubscriptionKind.EveryInstance,
            InstanceId = Guid.NewGuid(),
        };
}
