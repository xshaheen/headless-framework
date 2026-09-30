// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("Pulsar")]
public sealed class PulsarEveryInstanceTests(PulsarFixture fixture) : TestBase
{
    [Fact]
    public Task should_deliver_every_bus_message_to_every_every_instance_replica()
    {
        return TransportProviderConformance.AssertBusEveryInstanceAsync(
            new PulsarProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public async Task should_fan_out_one_bus_message_to_two_every_instance_sessions()
    {
        // given
        var logicalName = $"every-{Guid.NewGuid():N}";
        await using var first = await fixture.CreateEndpointSessionAsync(_Endpoint(logicalName), AbortToken);
        await using var second = await fixture.CreateEndpointSessionAsync(_Endpoint(logicalName), AbortToken);

        // when / then
        await TransportBusConformance.AssertEveryInstanceFanOutAsync(first, second, AbortToken);
    }

    [Fact]
    public async Task should_leave_no_subscription_on_the_topic_after_dispose()
    {
        // given
        var endpoint = _Endpoint($"every-{Guid.NewGuid():N}");
        var subscription = PulsarPhysicalAddress.Subscription(endpoint.ToRequest());
        using var admin = new HttpClient { BaseAddress = fixture.AdminUri };
        var session = await fixture.CreateEndpointSessionAsync(endpoint, AbortToken);

        try
        {
            await session.StartAsync(cancellationToken: AbortToken);
            (await _GetSubscriptionsAsync(admin, endpoint.LogicalName))
                .Should()
                .Contain(subscription, "the broker holds the subscription while the process consumes it");
        }
        finally
        {
            // when
            await session.DisposeAsync();
        }

        // then
        using var timeout = TimeSpan.FromSeconds(15).ToCancellationTokenSource(AbortToken);
        while (
            (await _GetSubscriptionsAsync(admin, endpoint.LogicalName)).Contains(subscription, StringComparer.Ordinal)
        )
        {
            await Task.Delay(100, timeout.Token);
        }
    }

    [Fact]
    public async Task should_report_the_subscription_reestablished_after_the_broker_moves_the_topic()
    {
        // given
        var endpoint = _Endpoint($"every-{Guid.NewGuid():N}");
        using var admin = new HttpClient { BaseAddress = fixture.AdminUri };
        await using var session = await fixture.CreateEndpointSessionAsync(endpoint, AbortToken);
        var reestablished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Consumer.AttachReestablishedCallback(_ =>
        {
            reestablished.TrySetResult();
            return Task.CompletedTask;
        });
        await session.StartAsync(cancellationToken: AbortToken);

        // when: unloading the topic closes every consumer on it, and Pulsar.Client reconnects on its own
        using var unload = await admin.PutAsync(
            _TopicPath(endpoint.LogicalName) + "/unload",
            content: null,
            AbortToken
        );
        unload.EnsureSuccessStatusCode();

        // then
        await reestablished.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        var message = _Message(endpoint.LogicalName);
        (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(20), AbortToken);
        delivery.Message.Id.Should().Be(message.Id, "the recovered subscription receives again");
    }

    private static TransportConformanceEndpoint _Endpoint(string logicalName) =>
        new(MessageLane.Bus, logicalName, "conformance.every-instance", "replica")
        {
            Kind = ConsumerSubscriptionKind.EveryInstance,
            InstanceId = Guid.NewGuid(),
        };

    private static string _TopicPath(string logicalName) =>
        $"admin/v2/persistent/public/default/{PulsarPhysicalAddress.Topic(MessageLane.Bus, logicalName)}";

    private async Task<string[]> _GetSubscriptionsAsync(HttpClient admin, string logicalName)
    {
        using var response = await admin.GetAsync(_TopicPath(logicalName) + "/subscriptions", AbortToken);

        // An unused topic may be garbage-collected once its last subscription goes, which leaves nothing to list.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<string[]>(AbortToken) ?? [];
    }

    private static TransportMessage _Message(string logicalName) =>
        new(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
                [MessagingHeaders.MessageName] = logicalName,
                [MessagingHeaders.Intent] = nameof(MessageLane.Bus),
            },
            "every-instance"u8.ToArray()
        );
}
