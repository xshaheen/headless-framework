// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

[Collection("Kafka")]
public sealed class KafkaRawPayloadTests(KafkaFixture fixture) : TestBase
{
    private const string _ContractVersion = "2";

    [Fact]
    public async Task should_consume_a_native_record_once_across_a_redelivery_when_its_contract_is_raw()
    {
        // given: a raw contract whose topic is its message name, and a consumer that counts every handler run
        var topic = $"raw-runs-{Guid.NewGuid():N}";
        var group = KafkaConsumerClient.GroupId(topic);
        var consumer = new RawRunConsumer();

        await using var firstHost = _BuildHost(topic, consumer, sharedStorage: null);
        var storage = firstHost.GetRequiredService<IDataStorage>();
        var firstBootstrapper = firstHost.GetRequiredService<IBootstrapper>();
        await firstBootstrapper.BootstrapAsync(AbortToken);

        using var producer = new ProducerBuilder<string, byte[]>(
            new ProducerConfig { BootstrapServers = fixture.ConnectionString }
        ).Build();

        // when: a non-.NET producer publishes with the native client, sending no Headless headers
        var valid = await producer.ProduceAsync(
            topic,
            new Message<string, byte[]> { Key = "run-1", Value = """{"RunId":"run-1","Score":7}"""u8.ToArray() },
            AbortToken
        );
        var malformed = await producer.ProduceAsync(
            topic,
            new Message<string, byte[]> { Key = "run-2", Value = "{not json"u8.ToArray() },
            AbortToken
        );

        // then: the handler sees the record under a position id, and the malformed body becomes one poison row
        await _WaitUntilAsync(() => consumer.Runs.Count == 1, "the handler ran");
        var run = consumer.Runs.Single();
        run.MessageId.Should().Be($"kafka:{topic}:0:{valid.Offset.Value}:{valid.Timestamp.UnixTimestampMs}");
        run.MessageName.Should().Be(topic);
        run.ContractVersion.Should().Be(_ContractVersion);
        run.Message.Should().Be(new RawRun("run-1", 7));
        run.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(valid.Timestamp.UnixTimestampMs));

        await _WaitUntilAsync(async () => await _CountPoisonRowsAsync(storage, topic) == 1, "the poison row");
        await _WaitUntilAsync(
            () => _CommittedOffset(group, topic) > malformed.Offset.Value,
            "the first host committed both records"
        );

        // when: a second process sharing the inbox receives the same records again from the broker
        await firstBootstrapper.DisposeAsync();
        await _RewindGroupAsync(group, topic, valid.Offset.Value);

        await using var secondHost = _BuildHost(topic, consumer, storage);
        secondHost.GetRequiredService<IDataStorage>().Should().BeSameAs(storage);
        await secondHost.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await _WaitUntilAsync(
            () => _CommittedOffset(group, topic) > malformed.Offset.Value,
            "the second host committed both records"
        );

        // then: the redelivery carries the same id, so the inbox runs the handler once, and the poison stays one row
        consumer.Runs.Should().ContainSingle();
        (await _CountPoisonRowsAsync(storage, topic)).Should().Be(1);
    }

    private ServiceProvider _BuildHost(string topic, RawRunConsumer consumer, IDataStorage? sharedStorage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseKafka(options =>
            {
                options.Servers = fixture.ConnectionString;
                options.TopicOptions.NumPartitions = 1;
                options.TopicOptions.ReplicationFactor = 1;
                options.MainConfig["allow.auto.create.topics"] = "true";
                options.MainConfig["auto.commit.interval.ms"] = "100";
            });
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<RawRun>(topic, _ContractVersion).OnQueue(queue => queue.RawPayload());
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

    private static async Task<int> _CountPoisonRowsAsync(IDataStorage storage, string topic)
    {
        var page = await storage
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Subscribe,
                    Name = topic,
                    StatusName = StatusName.Failed,
                    PageSize = 10,
                },
                AbortToken
            );

        return page.Items.Count;
    }

    private long _CommittedOffset(string group, string topic)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(
            new ConsumerConfig { BootstrapServers = fixture.ConnectionString, GroupId = group }
        ).Build();

        var committed = consumer.Committed([new TopicPartition(topic, new Partition(0))], TimeSpan.FromSeconds(5));

        return committed[0].Offset.Value;
    }

    private async Task _RewindGroupAsync(string group, string topic, long offset)
    {
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = fixture.ConnectionString }
        ).Build();

        // The group accepts new offsets only once the stopped consumer has left it.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                await admin.AlterConsumerGroupOffsetsAsync([
                    new ConsumerGroupTopicPartitionOffsets(
                        group,
                        [new TopicPartitionOffset(topic, new Partition(0), new Offset(offset))]
                    ),
                ]);

                return;
            }
            catch (AlterConsumerGroupOffsetsException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
            }
        }
    }

    private static Task _WaitUntilAsync(Func<bool> predicate, string what) =>
        _WaitUntilAsync(() => Task.FromResult(predicate()), what);

    private static async Task _WaitUntilAsync(Func<Task<bool>> predicate, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (!await predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
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
                "tests.kafka.raw-runs",
                TestConsumerDispatch.FromServices<RawRunConsumer, RawRun>()
            );
    }
}
