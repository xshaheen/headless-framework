// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Pending-entry recovery, idle-consumer cleanup, stream trimming, batch reads, and the entry wire format, against a
/// real Redis server.
/// </summary>
[Collection<RedisMessagingFixture>]
public sealed class RedisRecoveryRetentionTests(RedisMessagingFixture fixture) : TestBase
{
    [Fact]
    public async Task should_claim_an_entry_a_crashed_consumer_left_pending_after_the_min_idle_time()
    {
        // given: a consumer of another process read the entry and died without acknowledging it
        var (destination, stream) = _NewQueue();
        var database = (await fixture.GetAdminConnectionAsync()).GetDatabase();
        await database.StreamCreateConsumerGroupAsync(stream, destination, StreamPosition.NewMessages);
        var message = _Message(destination);
        await database.StreamAddAsync(stream, message.AsStreamEntries());
        await database.StreamReadGroupAsync(stream, destination, "crashed-host", StreamPosition.NewMessages);

        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken
        );

        // when
        await session.StartAsync(cancellationToken: AbortToken);
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        // then
        delivery.Message.Id.Should().Be(message.Id);
        (await database.StreamPendingAsync(stream, destination)).PendingMessageCount.Should().Be(0);
    }

    [Fact]
    public async Task should_resume_its_own_pending_entry_at_once_when_restarted_on_the_same_machine()
    {
        // given: this machine's first consumer slot read the entry before the process stopped
        var (destination, stream) = _NewQueue();
        var database = (await fixture.GetAdminConnectionAsync()).GetDatabase();
        await database.StreamCreateConsumerGroupAsync(stream, destination, StreamPosition.NewMessages);
        var message = _Message(destination);
        await database.StreamAddAsync(stream, message.AsStreamEntries());
        await database.StreamReadGroupAsync(
            stream,
            destination,
            $"{destination}:{Environment.MachineName}:0",
            StreamPosition.NewMessages
        );

        // A claim this late can never fire during the test, so only the startup pass can deliver the entry.
        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken,
            configure: options => options.PendingClaimMinIdleTime = TimeSpan.FromHours(1)
        );

        // when
        await session.StartAsync(cancellationToken: AbortToken);
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(5), AbortToken);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        // then
        delivery.Message.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task should_leave_a_rejected_entry_pending_in_place_and_deliver_it_again()
    {
        // given
        var (destination, stream) = _NewQueue();
        var database = (await fixture.GetAdminConnectionAsync()).GetDatabase();
        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken
        );
        await session.StartAsync(cancellationToken: AbortToken);
        var message = _Message(destination);
        (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
        var first = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        await session.Consumer.RejectAsync(first.SettlementValue, AbortToken);
        var lengthAfterReject = await database.StreamLengthAsync(stream);
        var pendingAfterReject = (await database.StreamPendingAsync(stream, destination)).PendingMessageCount;
        var redelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await session.Consumer.CommitAsync(redelivery.SettlementValue, AbortToken);

        // then
        lengthAfterReject.Should().Be(1);
        pendingAfterReject.Should().Be(1);
        redelivery.Message.Id.Should().Be(message.Id);
        (await database.StreamLengthAsync(stream)).Should().Be(1);
    }

    [Fact]
    public async Task should_delete_idle_consumers_without_pending_entries_and_keep_those_holding_one()
    {
        // given
        var (destination, stream) = _NewQueue();
        var database = (await fixture.GetAdminConnectionAsync()).GetDatabase();
        await database.StreamCreateConsumerGroupAsync(stream, destination, StreamPosition.NewMessages);
        await database.StreamReadGroupAsync(stream, destination, "gone-empty", StreamPosition.NewMessages);
        await database.StreamAddAsync(stream, _Message(destination).AsStreamEntries());
        await database.StreamReadGroupAsync(stream, destination, "gone-holding", StreamPosition.NewMessages);
        await Task.Delay(TimeSpan.FromMilliseconds(300), AbortToken);

        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken,
            configure: options =>
            {
                options.PendingClaimMinIdleTime = TimeSpan.FromHours(1);
                options.StreamMaxAge = TimeSpan.Zero;
                options.IdleConsumerDeleteAfter = TimeSpan.FromMilliseconds(200);
            }
        );

        // when
        await session.StartAsync(cancellationToken: AbortToken);
        var names = await _ConsumerNamesAsync(database, stream, destination);
        using (var timeout = TimeSpan.FromSeconds(10).ToCancellationTokenSource(AbortToken))
        {
            while (names.Contains("gone-empty"))
            {
                await Task.Delay(50, timeout.Token);
                names = await _ConsumerNamesAsync(database, stream, destination);
            }
        }

        // then
        names.Should().NotContain("gone-empty");
        names.Should().Contain("gone-holding");
    }

    [Fact]
    public async Task should_trim_entries_older_than_the_max_age_when_publishing()
    {
        // given: two full internal nodes of entries stamped at the epoch, far older than any age limit
        var (destination, stream) = _NewQueue();
        var database = (await fixture.GetAdminConnectionAsync()).GetDatabase();
        for (var i = 1; i <= 200; i++)
        {
            await database.StreamAddAsync(stream, "old", i, messageId: $"1-{i}");
        }

        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken,
            configure: options => options.StreamMaxAge = TimeSpan.FromDays(1)
        );
        var message = _Message(destination);

        // when
        (await session.PublishAsync(message, AbortToken))
            .Succeeded.Should()
            .BeTrue();

        // then: approximate trimming drops whole nodes only, so it removes the full old nodes and keeps the new entry
        var remaining = await database.StreamRangeAsync(stream);
        remaining.Should().HaveCountLessThan(201);
        remaining.Should().NotContain(entry => entry.Id == "1-1");
        RedisMessage.Create(remaining[^1]).Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task should_drain_a_backlog_without_waiting_a_poll_interval_per_full_batch()
    {
        // given: a poll interval far longer than the test, so only back-to-back reads can deliver the backlog
        var (destination, _) = _NewQueue();
        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken,
            configure: options => options.StreamEntriesCount = 2,
            listeningTimeout: TimeSpan.FromMinutes(5)
        );
        var messages = Enumerable.Range(0, 5).Select(_ => _Message(destination)).ToArray();
        foreach (var message in messages)
        {
            (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
        }

        // when
        await session.StartAsync(
            delivery => session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken).AsTask(),
            cancellationToken: AbortToken
        );
        List<string> received = [];
        for (var i = 0; i < messages.Length; i++)
        {
            received.Add((await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken)).Message.Id);
        }

        // then
        received.Should().BeEquivalentTo(messages.Select(message => message.Id));
    }

    [Fact]
    public async Task should_store_the_body_as_raw_bytes_in_the_stream_entry()
    {
        // given
        var (destination, stream) = _NewQueue();
        var database = (await fixture.GetAdminConnectionAsync()).GetDatabase();
        await using var session = await fixture.CreateSessionAsync(
            MessageLane.Queue,
            destination,
            destination,
            AbortToken
        );
        byte[] body = [0, 255, 34, 92, 0, 128];

        // when
        (await session.PublishAsync(_Message(destination, body), AbortToken))
            .Succeeded.Should()
            .BeTrue();

        // then
        var entry = (await database.StreamRangeAsync(stream)).Should().ContainSingle().Which;
        ((byte[])entry["body"]!).Should().Equal(body);
    }

    private static (string Destination, string Stream) _NewQueue()
    {
        var destination = $"recovery-{Guid.NewGuid():N}";
        return (destination, RedisPhysicalAddress.QueueStream(destination));
    }

    private static TransportMessage _Message(string destination, byte[]? body = null)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Guid.NewGuid().ToString("N"),
                [Headers.MessageName] = destination,
                [Headers.Intent] = nameof(MessageLane.Queue),
            },
            body ?? "recovery"u8.ToArray()
        );
    }

    private static async Task<string[]> _ConsumerNamesAsync(IDatabase database, string stream, string group)
    {
        return [.. (await database.StreamConsumerInfoAsync(stream, group)).Select(consumer => consumer.Name)];
    }
}
