// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Pulsar.Client.Api;
using Pulsar.Client.Common;
using Tests.Helpers;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("Pulsar")]
public sealed class PulsarConsumerHatchTests(PulsarFixture fixture) : TestBase
{
    private const string _QueueSubscription = "headless-queue";

    [Fact]
    public async Task should_subscribe_key_shared_when_a_declared_consumer_is_tuned_with_key_shared()
    {
        // given
        var name = $"hatch-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UsePulsar(fixture.ConnectionString);
            setup.UseInMemoryStorage();
            setup.Tune(KeySharedModule.Identity, consumer => consumer.UsePulsar(pulsar => pulsar.KeyShared()));
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<KeySharedProbe>(name);
            messaging.AddModule<KeySharedModule>();
        });
        await using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IConsumerRegistry>();
        var factory = provider.GetRequiredService<IConsumerClientFactory>();

        // when
        await using var client = await factory.CreateAsync(
            new ConsumerClientRequest(name, 1, MessageLane.Queue),
            AbortToken
        );
        await client.SubscribeAsync([name], AbortToken);

        // then
        (await _GetSubscriptionTypeAsync(PulsarPhysicalAddress.Topic(MessageLane.Queue, name)))
            .Should()
            .Be("Key_Shared");
    }

    [Fact]
    public async Task should_route_each_key_to_one_key_shared_replica_when_mixed_key_messages_share_a_batch()
    {
        // given
        var destination = $"hatch-{Guid.NewGuid():N}";
        var config = new PulsarConsumerConfig(KeyShared: true);
        await using var first = await fixture.CreateTunedQueueSessionAsync(destination, config, AbortToken);
        await using var second = await fixture.CreateTunedQueueSessionAsync(destination, config, AbortToken);

        const int keyCount = 32;
        const int messagesPerKey = 3;
        const int total = keyCount * messagesPerKey;
        var received = new ConcurrentQueue<(int Replica, string Key)>();
        var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<TransportConformanceDelivery, Task> recordInto(int replica, TransportConsumerConformanceSession session) =>
            async delivery =>
            {
                received.Enqueue((replica, delivery.Message.Headers[PulsarMessagingHeaders.PulsarKey]!));
                await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

                if (received.Count >= total)
                {
                    allReceived.TrySetResult();
                }
            };

        await first.StartAsync(recordInto(1, first), cancellationToken: AbortToken);
        await second.StartAsync(recordInto(2, second), cancellationToken: AbortToken);

        // when: concurrent sends land in shared producer batches, so a batch carries several keys
        var sends = Enumerable
            .Range(0, total)
            .Select(i => first.PublishAsync(_Message(destination, key: $"key-{i % keyCount}"), AbortToken));
        var results = await Task.WhenAll(sends);

        // then
        results.Should().OnlyContain(result => result.Succeeded);
        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        var replicasByKey = received
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Replica).ToHashSet());
        replicasByKey.Should().HaveCount(keyCount);
        replicasByKey
            .Values.Should()
            .OnlyContain(replicas => replicas.Count == 1, "Key_Shared sends one key to one consumer");
        received.Select(x => x.Replica).Distinct().Should().HaveCount(2, "both replicas own part of the key range");
        (await _GetSubscriptionTypeAsync(PulsarPhysicalAddress.Topic(MessageLane.Queue, destination)))
            .Should()
            .Be("Key_Shared");
    }

    [Fact]
    public async Task should_move_a_message_rejected_past_its_redelivery_limit_to_the_dead_letter_topic()
    {
        // given
        var destination = $"hatch-{Guid.NewGuid():N}";
        await using var session = await fixture.CreateTunedQueueSessionAsync(
            destination,
            new PulsarConsumerConfig(KeyShared: false, MaxRedeliveryCount: 2),
            AbortToken
        );
        await session.StartAsync(cancellationToken: AbortToken);
        var message = _Message(destination);

        // The default dead-letter topic is named after the fully qualified physical topic and the subscription.
        var deadLetterTopic =
            $"persistent://public/default/{PulsarPhysicalAddress.Topic(MessageLane.Queue, destination)}-{_QueueSubscription}-DLQ";
        var client = await new PulsarClientBuilder().ServiceUrl(fixture.ConnectionString).BuildAsync();

        try
        {
            await using var deadLetters = await client
                .NewConsumer()
                .Topic(deadLetterTopic)
                .SubscriptionName("probe")
                .SubscriptionInitialPosition(SubscriptionInitialPosition.Earliest)
                .SubscribeAsync()
                .WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

            (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();

            // when: the first delivery and two redeliveries are all rejected
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(20), AbortToken);
                delivery.Message.Id.Should().Be(message.Id);
                await session.Consumer.RejectAsync(delivery.SettlementValue, AbortToken);
            }

            // then
            var deadLetter = await deadLetters.ReceiveAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
            deadLetter.Properties[MessagingHeaders.MessageId].Should().Be(message.Id);
            await deadLetters.AcknowledgeAsync(deadLetter.MessageId);
            (await session.RemainsEmptyAsync(TimeSpan.FromSeconds(3), AbortToken))
                .Should()
                .BeTrue("a dead-lettered message is not redelivered to the subscription");
        }
        finally
        {
            await client.CloseAsync();
        }
    }

    [Fact]
    public async Task should_redeliver_a_message_left_unacknowledged_past_the_ack_timeout()
    {
        // given
        var destination = $"hatch-{Guid.NewGuid():N}";
        await using var session = await fixture.CreateTunedQueueSessionAsync(
            destination,
            new PulsarConsumerConfig(KeyShared: false, AckTimeout: TimeSpan.FromSeconds(1)),
            AbortToken
        );
        await session.StartAsync(cancellationToken: AbortToken);
        var message = _Message(destination);

        // when
        (await session.PublishAsync(message, AbortToken))
            .Succeeded.Should()
            .BeTrue();
        var first = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        var redelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        first.Message.Id.Should().Be(message.Id);
        redelivery.Message.Id.Should().Be(message.Id, "an unacknowledged message is redelivered after the ack timeout");
        await session.Consumer.CommitAsync(redelivery.SettlementValue, AbortToken);
    }

    private async Task<string?> _GetSubscriptionTypeAsync(string physicalTopic)
    {
        using var admin = new HttpClient { BaseAddress = fixture.AdminUri };
        using var stats = await admin.GetFromJsonAsync<JsonDocument>(
            $"admin/v2/persistent/public/default/{physicalTopic}/stats",
            AbortToken
        );

        return stats!
            .RootElement.GetProperty("subscriptions")
            .GetProperty(_QueueSubscription)
            .GetProperty("type")
            .GetString();
    }

    private static TransportMessage _Message(string destination, string? key = null)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
            [MessagingHeaders.MessageName] = destination,
            [MessagingHeaders.Intent] = nameof(MessageLane.Queue),
        };

        if (key is not null)
        {
            headers[PulsarMessagingHeaders.PulsarKey] = key;
        }

        return new TransportMessage(headers, "hatch"u8.ToArray());
    }

    public sealed record KeySharedProbe(string Value);

    public sealed class KeySharedModule : IMessagingModule
    {
        public const string Identity = "tests.pulsar.key-shared";

        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<KeySharedConsumer, KeySharedProbe>(
                Identity,
                TestConsumerDispatch.FromServices<KeySharedConsumer, KeySharedProbe>()
            );
    }

    public sealed class KeySharedConsumer : IConsume<KeySharedProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<KeySharedProbe> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
