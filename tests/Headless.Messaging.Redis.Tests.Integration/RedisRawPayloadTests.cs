// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// An entry a non-.NET producer writes with its own Redis client, carrying only a <c>body</c> field, reaches a consumer
/// whose contract accepts raw payloads.
/// </summary>
[Collection<RedisMessagingFixture>]
public sealed class RedisRawPayloadTests(RedisMessagingFixture fixture) : TestBase
{
    [Fact]
    public async Task should_consume_a_headerless_entry_once_and_poison_a_malformed_body()
    {
        // given
        var name = $"raw-{Guid.NewGuid():N}";
        var stream = RedisPhysicalAddress.QueueStream(name);
        var database = (await fixture.GetAdminConnectionAsync()).GetDatabase();
        await using var provider = await _StartHostAsync(name);
        var recorder = provider.GetRequiredService<RawProbeRecorder>();
        var group = await _WaitForConsumerGroupAsync(database, stream);

        // when: a foreign producer appends an entry with no headers field
        var entryId = (await database.StreamAddAsync(stream, "body", """{"value":"from-python"}""")).ToString();
        var received = await recorder.NextAsync(TimeSpan.FromSeconds(15), AbortToken);

        // then
        received.Value.Should().Be("from-python");
        received.MessageId.Should().Be($"redis:{stream}:{entryId}");
        await _WaitUntilAsync(async () => (await database.StreamPendingAsync(stream, group)).PendingMessageCount == 0);

        // when: the group is rewound, so Redis delivers the same entry again
        await database.ExecuteAsync("XGROUP", "SETID", stream, group, "0");
        await _WaitUntilAsync(async () =>
            string.Equals(
                (await database.StreamGroupInfoAsync(stream)).Single().LastDeliveredId,
                entryId,
                StringComparison.Ordinal
            )
            && (await database.StreamPendingAsync(stream, group)).PendingMessageCount == 0
        );
        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);

        // then: the inbox recognizes the message id and the handler does not run again
        recorder.Count.Should().Be(1);

        // when: a foreign producer appends an entry whose body is not JSON
        var malformedId = (await database.StreamAddAsync(stream, "body", "not-json")).ToString();

        // then: it is stored as poisoned, settled, and never handled
        var storage = provider.GetRequiredService<IDataStorage>();
        await _WaitUntilAsync(async () => await _CountFailedAsync(storage, name) == 1);
        await _WaitUntilAsync(async () => (await database.StreamPendingAsync(stream, group)).PendingMessageCount == 0);
        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
        recorder.Count.Should().Be(1);
        (await _CountFailedAsync(storage, name)).Should().Be(1);
        malformedId.Should().NotBe(entryId);
    }

    private async Task<ServiceProvider> _StartHostAsync(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RawProbeRecorder>();
        services.AddSingleton<RawProbeConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRedis(fixture.ConnectionString);
            setup.UseInMemoryStorage();

            // In-memory storage deduplicates within the process, which is all one host in a test needs.
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;

            // The foreign producer writes camelCase JSON; the default options match property names exactly.
            setup.Options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<RawProbe>(name).OnQueue(queue => queue.RawPayload());
            messaging.AddModule<RawProbeModule>();
        });

        var provider = services.BuildServiceProvider();
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        return provider;
    }

    private async Task<string> _WaitForConsumerGroupAsync(IDatabase database, string stream)
    {
        string? group = null;
        await _WaitUntilAsync(async () =>
        {
            if (!await database.KeyExistsAsync(stream))
            {
                return false;
            }

            group = (await database.StreamGroupInfoAsync(stream)).FirstOrDefault().Name;
            return group is not null;
        });

        return group!;
    }

    private static async Task<long> _CountFailedAsync(IDataStorage storage, string name)
    {
        var page = await storage
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Subscribe,
                    Name = name,
                    StatusName = StatusName.Failed,
                    CurrentPage = 0,
                    PageSize = 10,
                }
            );

        return page.TotalItems;
    }

    private async Task _WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = TimeSpan.FromSeconds(15).ToCancellationTokenSource(AbortToken);

        while (!await condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }

    public sealed record RawProbe(string Value);

    public sealed record RawDelivery(string Value, string MessageId);

    public sealed class RawProbeRecorder : IDisposable
    {
        private readonly ConcurrentQueue<RawDelivery> _deliveries = new();
        private readonly SemaphoreSlim _arrived = new(0);

        public int Count => _deliveries.Count;

        public void Dispose() => _arrived.Dispose();

        public void Record(RawDelivery delivery)
        {
            _deliveries.Enqueue(delivery);
            _arrived.Release();
        }

        public async Task<RawDelivery> NextAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            (await _arrived.WaitAsync(timeout, cancellationToken)).Should().BeTrue("the raw entry should be consumed");
            return _deliveries.Last();
        }
    }

    public sealed class RawProbeConsumer(RawProbeRecorder recorder) : IConsume<RawProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<RawProbe> context, CancellationToken cancellationToken)
        {
            recorder.Record(new RawDelivery(context.Message.Value, context.MessageId));
            return ValueTask.CompletedTask;
        }
    }

    public sealed class RawProbeModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<RawProbeConsumer, RawProbe>(
                "tests.redis.raw-probe",
                TestConsumerDispatch.FromServices<RawProbeConsumer, RawProbe>()
            );
    }
}
