// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Pulsar.Client.Api;
using Tests.Helpers;

namespace Tests;

[Collection("Pulsar")]
public sealed class PulsarRawPayloadTests(PulsarFixture fixture) : TestBase
{
    private const string _ContractVersion = "2";
    private const string _QueueSubscription = "headless-queue";

    [Fact]
    public async Task should_consume_a_native_message_once_across_a_redelivery_when_its_contract_is_raw()
    {
        // given: a raw contract whose topic Headless derives from its name, and a consumer that counts every run
        var name = $"raw-runs-{Guid.NewGuid():N}";
        var topic = PulsarPhysicalAddress.Topic(MessageLane.Queue, name);
        var completeTopic = $"persistent://public/default/{topic}";
        var consumer = new RawRunConsumer();

        await using var firstHost = _BuildHost(name, consumer, sharedStorage: null);
        var storage = firstHost.GetRequiredService<IDataStorage>();
        var firstBootstrapper = firstHost.GetRequiredService<IBootstrapper>();
        await firstBootstrapper.BootstrapAsync(AbortToken);
        await _WaitUntilAsync(async () => await _BacklogAsync(topic) is not null, "the queue subscription");

        var client = await new PulsarClientBuilder().ServiceUrl(fixture.ConnectionString).BuildAsync();
        global::Pulsar.Client.Common.MessageId valid;

        try
        {
            // Batching off, so the broker stores each message as its own entry and the expected position is exact.
            await using var producer = await client
                .NewProducer()
                .Topic(topic)
                .EnableBatching(false)
                .CreateAsync()
                .WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

            // when: a non-.NET producer publishes with the native client, sending no Headless properties
            valid = await producer.SendAsync("""{"RunId":"run-1","Score":7}"""u8.ToArray());
            await producer.SendAsync("{not json"u8.ToArray());
        }
        finally
        {
            await client.CloseAsync();
        }

        // then: the handler sees the message under its broker position, and the malformed body becomes one poison row
        await _WaitUntilAsync(() => Task.FromResult(consumer.Runs.Count == 1), "the handler ran");
        var run = consumer.Runs.Single();
        run.MessageId.Should().Be($"pulsar:{completeTopic}:{valid.LedgerId}:{valid.EntryId}:-1:-1");
        run.MessageName.Should().Be(name);
        run.ContractVersion.Should().Be(_ContractVersion);
        run.Message.Should().Be(new RawRun("run-1", 7));
        run.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        await _WaitUntilAsync(async () => await _CountPoisonRowsAsync(storage, name) == 1, "the poison row");
        await _WaitUntilAsync(async () => await _BacklogAsync(topic) == 0, "the first host acknowledged both");

        // when: a second process sharing the inbox receives the same stored messages again from the broker
        await firstBootstrapper.DisposeAsync();
        await _RewindSubscriptionAsync(topic);
        (await _BacklogAsync(topic)).Should().Be(2, "the rewound subscription holds both messages again");

        await using var secondHost = _BuildHost(name, consumer, storage);
        await secondHost.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        await _WaitUntilAsync(async () => await _BacklogAsync(topic) == 0, "the second host acknowledged both");

        // then: the redelivery carries the same id, so the inbox runs the handler once, and the poison stays one row
        consumer.Runs.Should().ContainSingle();
        (await _CountPoisonRowsAsync(storage, name)).Should().Be(1);
    }

    private ServiceProvider _BuildHost(string name, RawRunConsumer consumer, IDataStorage? sharedStorage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UsePulsar(fixture.ConnectionString);
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<RawRun>(name, _ContractVersion).OnQueue(queue => queue.RawPayload());
            messaging.AddModule<RawRunConsumerModule>();
        });
        services.AddSingleton(consumer);

        if (sharedStorage is not null)
        {
            // Two processes share one durable inbox in production; the in-memory store stands in for it by handing
            // the second host the first host's instance, which every storage service resolves through.
            services.AddSingleton(sharedStorage.GetType(), sharedStorage);
        }

        return services.BuildServiceProvider();
    }

    private static async Task<int> _CountPoisonRowsAsync(IDataStorage storage, string name)
    {
        var page = await storage
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Subscribe,
                    Name = name,
                    StatusName = StatusName.Failed,
                    PageSize = 10,
                },
                AbortToken
            );

        return page.Items.Count;
    }

    // The queue subscription's backlog: messages the broker holds for it that no consumer acknowledged yet, or null
    // before the subscription exists.
    private async Task<long?> _BacklogAsync(string topic)
    {
        using var admin = new HttpClient { BaseAddress = fixture.AdminUri };
        using var response = await admin.GetAsync($"admin/v2/persistent/public/default/{topic}/stats", AbortToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stats = await response.Content.ReadFromJsonAsync<JsonDocument>(AbortToken);

        return stats!.RootElement.GetProperty("subscriptions").TryGetProperty(_QueueSubscription, out var subscription)
            ? subscription.GetProperty("msgBacklog").GetInt64()
            : null;
    }

    // Moves the subscription's cursor back to the first message, which a broker accepts once no consumer is attached.
    private async Task _RewindSubscriptionAsync(string topic)
    {
        using var admin = new HttpClient { BaseAddress = fixture.AdminUri };

        await _WaitUntilAsync(
            async () =>
            {
                using var response = await admin.PostAsync(
                    $"admin/v2/persistent/public/default/{topic}/subscription/{_QueueSubscription}/resetcursor/0",
                    content: null,
                    AbortToken
                );

                return response.IsSuccessStatusCode;
            },
            "the subscription cursor rewind"
        );
    }

    private static async Task _WaitUntilAsync(Func<Task<bool>> predicate, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (!await predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new System.TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);
        }
    }

    public sealed record RawRun(string RunId, int Score);

    private sealed record ObservedRun(
        string MessageId,
        string MessageName,
        string ContractVersion,
        RawRun Message,
        DateTimeOffset Timestamp
    );

    private sealed class RawRunConsumer : IConsume<RawRun>
    {
        public ConcurrentQueue<ObservedRun> Runs { get; } = new();

        public ValueTask ConsumeAsync(ConsumeContext<RawRun> context, CancellationToken cancellationToken)
        {
            Runs.Enqueue(
                new ObservedRun(
                    context.MessageId,
                    context.MessageName,
                    context.ContractVersion,
                    context.Message,
                    context.Timestamp
                )
            );

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RawRunConsumerModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<RawRunConsumer, RawRun>(
                "tests.pulsar.raw-runs",
                TestConsumerDispatch.FromServices<RawRunConsumer, RawRun>()
            );
    }
}
