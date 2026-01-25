// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection("InMemoryStorage")]
public sealed class InMemoryDataStorageTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ISerializer _serializer = Substitute.For<ISerializer>();
    private readonly ILongIdGenerator _idGenerator = Substitute.For<ILongIdGenerator>();
    private readonly IOptions<MessagingOptions> _options = Options.Create(new MessagingOptions { FailedRetryCount = 3 });
    private readonly InMemoryDataStorage _sut;
    private long _idCounter = 1000;

    public InMemoryDataStorageTests()
    {
        // Clear static state before each test
        InMemoryDataStorage.PublishedMessages.Clear();
        InMemoryDataStorage.ReceivedMessages.Clear();

        _timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
        _serializer.Serialize(Arg.Any<Message>()).Returns(call => JsonSerializer.Serialize(call.Arg<Message>()));
        _idGenerator.Create().Returns(_ => Interlocked.Increment(ref _idCounter));

        _sut = new InMemoryDataStorage(_options, _serializer, _idGenerator, _timeProvider);
    }

    [Fact]
    public async Task should_store_published_message()
    {
        // given
        var message = _CreateMessage("msg-1");

        // when
        var result = await _sut.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);

        // then
        result.DbId.Should().Be("msg-1");
        result.Origin.Should().BeSameAs(message);
        result.Retries.Should().Be(0);
        result.Added.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);

        InMemoryDataStorage.PublishedMessages.Should().ContainKey("msg-1");
        InMemoryDataStorage.PublishedMessages["msg-1"].Name.Should().Be("test.topic");
        InMemoryDataStorage.PublishedMessages["msg-1"].StatusName.Should().Be(StatusName.Scheduled);
    }

    [Fact]
    public async Task should_store_received_message()
    {
        // given
        var message = _CreateMessage("msg-received");

        // when
        var result = await _sut.StoreReceivedMessageAsync("test.topic", "consumer-group", message, AbortToken);

        // then
        result.Origin.Should().BeSameAs(message);
        result.Retries.Should().Be(0);
        result.Added.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);

        var storedMessage = InMemoryDataStorage.ReceivedMessages.Values.First();
        storedMessage.Name.Should().Be("test.topic");
        storedMessage.Group.Should().Be("consumer-group");
        storedMessage.StatusName.Should().Be(StatusName.Scheduled);
    }

    [Fact]
    public async Task should_store_received_exception_message()
    {
        // given
        var content = "Error: Something went wrong";

        // when
        await _sut.StoreReceivedExceptionMessageAsync("failed.topic", "error-group", content, AbortToken);

        // then
        var storedMessage = InMemoryDataStorage.ReceivedMessages.Values.First();
        storedMessage.Name.Should().Be("failed.topic");
        storedMessage.Group.Should().Be("error-group");
        storedMessage.Content.Should().Be(content);
        storedMessage.StatusName.Should().Be(StatusName.Failed);
        storedMessage.Retries.Should().Be(_options.Value.FailedRetryCount);
        storedMessage.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task should_change_publish_state()
    {
        // given
        var message = _CreateMessage("state-change-msg");
        var stored = await _sut.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);

        // when
        await _sut.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // then
        InMemoryDataStorage.PublishedMessages["state-change-msg"].StatusName.Should().Be(StatusName.Succeeded);
        InMemoryDataStorage.PublishedMessages["state-change-msg"].ExpiresAt.Should().Be(stored.ExpiresAt);
    }

    [Fact]
    public async Task should_change_receive_state()
    {
        // given
        var message = _CreateMessage("receive-state-msg");
        var stored = await _sut.StoreReceivedMessageAsync("test.topic", "group", message, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);

        // when
        await _sut.ChangeReceiveStateAsync(stored, StatusName.Failed, AbortToken);

        // then
        InMemoryDataStorage.ReceivedMessages[stored.DbId].StatusName.Should().Be(StatusName.Failed);
    }

    [Fact]
    public async Task should_change_publish_state_to_delayed()
    {
        // given
        await _sut.StoreMessageAsync("topic", _CreateMessage("delay-1"), cancellationToken: AbortToken);
        await _sut.StoreMessageAsync("topic", _CreateMessage("delay-2"), cancellationToken: AbortToken);

        // when
        await _sut.ChangePublishStateToDelayedAsync(["delay-1", "delay-2"], AbortToken);

        // then
        InMemoryDataStorage.PublishedMessages["delay-1"].StatusName.Should().Be(StatusName.Delayed);
        InMemoryDataStorage.PublishedMessages["delay-2"].StatusName.Should().Be(StatusName.Delayed);
    }

    [Fact]
    public async Task should_acquire_lock_always_returns_true()
    {
        // when
        var result = await _sut.AcquireLockAsync("test-lock", TimeSpan.FromMinutes(5), "instance-1", AbortToken);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_release_lock_completes_successfully()
    {
        // when
        await _sut.ReleaseLockAsync("test-lock", "instance-1", AbortToken);

        // then - no exception thrown indicates success
    }

    [Fact]
    public async Task should_renew_lock_completes_successfully()
    {
        // when
        await _sut.RenewLockAsync("test-lock", TimeSpan.FromMinutes(10), "instance-1", AbortToken);

        // then - no exception thrown indicates success
    }

    [Fact]
    public async Task should_get_published_messages_for_retry()
    {
        // given
        _timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var msg1 = await _sut.StoreMessageAsync("topic", _CreateMessage("retry-1"), cancellationToken: AbortToken);
        var msg2 = await _sut.StoreMessageAsync("topic", _CreateMessage("retry-2"), cancellationToken: AbortToken);

        // Set one to failed status
        await _sut.ChangePublishStateAsync(msg2, StatusName.Failed, cancellationToken: AbortToken);

        // Advance time past lookback window
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var result = await _sut.GetPublishedMessagesOfNeedRetry(TimeSpan.FromMinutes(1), AbortToken);

        // then
        var messages = result.ToList();
        messages.Should().HaveCount(2);
        messages.Should().Contain(m => m.DbId == "retry-1");
        messages.Should().Contain(m => m.DbId == "retry-2");
    }

    [Fact]
    public async Task should_not_return_messages_with_max_retries_exceeded()
    {
        // given
        _timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
        await _sut.StoreMessageAsync("topic", _CreateMessage("max-retry"), cancellationToken: AbortToken);

        // Set retries to max
        InMemoryDataStorage.PublishedMessages["max-retry"].Retries = _options.Value.FailedRetryCount;

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var result = await _sut.GetPublishedMessagesOfNeedRetry(TimeSpan.FromMinutes(1), AbortToken);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_get_received_messages_for_retry()
    {
        // given
        _timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var msg = await _sut.StoreReceivedMessageAsync("topic", "group", _CreateMessage("recv-retry"), AbortToken);

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var result = await _sut.GetReceivedMessagesOfNeedRetry(TimeSpan.FromMinutes(1), AbortToken);

        // then
        result.Should().ContainSingle().Which.DbId.Should().Be(msg.DbId);
    }

    [Fact]
    public async Task should_delete_expired_published_messages()
    {
        // given
        await _sut.StoreMessageAsync("topic", _CreateMessage("expired-1"), cancellationToken: AbortToken);
        await _sut.StoreMessageAsync("topic", _CreateMessage("not-expired"), cancellationToken: AbortToken);

        InMemoryDataStorage.PublishedMessages["expired-1"].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);
        InMemoryDataStorage.PublishedMessages["not-expired"].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);

        // when
        var deleted = await _sut.DeleteExpiresAsync(
            nameof(InMemoryDataStorage.PublishedMessages),
            _timeProvider.GetUtcNow().UtcDateTime,
            batchCount: 100,
            cancellationToken: AbortToken
        );

        // then
        deleted.Should().Be(1);
        InMemoryDataStorage.PublishedMessages.Should().NotContainKey("expired-1");
        InMemoryDataStorage.PublishedMessages.Should().ContainKey("not-expired");
    }

    [Fact]
    public async Task should_delete_expired_received_messages()
    {
        // given
        var msg1 = await _sut.StoreReceivedMessageAsync("topic", "group", _CreateMessage("recv-expired"), AbortToken);
        var msg2 = await _sut.StoreReceivedMessageAsync("topic", "group", _CreateMessage("recv-not-expired"), AbortToken);

        InMemoryDataStorage.ReceivedMessages[msg1.DbId].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);
        InMemoryDataStorage.ReceivedMessages[msg2.DbId].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);

        // when
        var deleted = await _sut.DeleteExpiresAsync(
            nameof(InMemoryDataStorage.ReceivedMessages),
            _timeProvider.GetUtcNow().UtcDateTime,
            batchCount: 100,
            cancellationToken: AbortToken
        );

        // then
        deleted.Should().Be(1);
        InMemoryDataStorage.ReceivedMessages.Should().NotContainKey(msg1.DbId);
        InMemoryDataStorage.ReceivedMessages.Should().ContainKey(msg2.DbId);
    }

    [Fact]
    public async Task should_respect_batch_count_when_deleting_expires()
    {
        // given
        for (var i = 0; i < 10; i++)
        {
            var msg = await _sut.StoreMessageAsync("topic", _CreateMessage($"batch-{i}"), cancellationToken: AbortToken);
            InMemoryDataStorage.PublishedMessages[msg.DbId].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);
        }

        // when
        var deleted = await _sut.DeleteExpiresAsync(
            nameof(InMemoryDataStorage.PublishedMessages),
            _timeProvider.GetUtcNow().UtcDateTime,
            batchCount: 5,
            cancellationToken: AbortToken
        );

        // then
        deleted.Should().Be(5);
        InMemoryDataStorage.PublishedMessages.Should().HaveCount(5);
    }

    [Fact]
    public async Task should_delete_received_message_by_id()
    {
        // given
        var msg = await _sut.StoreReceivedMessageAsync("topic", "group", _CreateMessage("to-delete"), AbortToken);
        var id = long.Parse(msg.DbId, CultureInfo.InvariantCulture);

        // when
        var result = await _sut.DeleteReceivedMessageAsync(id, AbortToken);

        // then
        result.Should().Be(1);
        InMemoryDataStorage.ReceivedMessages.Should().NotContainKey(msg.DbId);
    }

    [Fact]
    public async Task should_return_zero_when_deleting_nonexistent_received_message()
    {
        // when
        var result = await _sut.DeleteReceivedMessageAsync(999999, AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_delete_published_message_by_id()
    {
        // given
        // Use a numeric ID since DeletePublishedMessageAsync takes a long
        var msg = await _sut.StoreMessageAsync("topic", _CreateMessage("12345"), cancellationToken: AbortToken);
        var id = long.Parse(msg.DbId, CultureInfo.InvariantCulture);

        // when
        var result = await _sut.DeletePublishedMessageAsync(id, AbortToken);

        // then
        result.Should().Be(1);
        InMemoryDataStorage.PublishedMessages.Should().NotContainKey("12345");
    }

    [Fact]
    public async Task should_schedule_delayed_messages()
    {
        // given
        var msg = await _sut.StoreMessageAsync("topic", _CreateMessage("delayed-msg"), cancellationToken: AbortToken);
        await _sut.ChangePublishStateAsync(msg, StatusName.Delayed, cancellationToken: AbortToken);
        InMemoryDataStorage.PublishedMessages["delayed-msg"].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(1);

        var scheduledMessages = new List<MediumMessage>();

        // when
        await _sut.ScheduleMessagesOfDelayedAsync(
            (_, messages) =>
            {
                scheduledMessages.AddRange(messages);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );

        // then
        scheduledMessages.Should().ContainSingle().Which.DbId.Should().Be("delayed-msg");
    }

    [Fact]
    public async Task should_schedule_queued_messages_past_expiry()
    {
        // given
        var msg = await _sut.StoreMessageAsync("topic", _CreateMessage("queued-msg"), cancellationToken: AbortToken);
        await _sut.ChangePublishStateAsync(msg, StatusName.Queued, cancellationToken: AbortToken);
        InMemoryDataStorage.PublishedMessages["queued-msg"].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-2);

        var scheduledMessages = new List<MediumMessage>();

        // when
        await _sut.ScheduleMessagesOfDelayedAsync(
            (_, messages) =>
            {
                scheduledMessages.AddRange(messages);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );

        // then
        scheduledMessages.Should().ContainSingle().Which.DbId.Should().Be("queued-msg");
    }

    [Fact]
    public void should_return_monitoring_api()
    {
        // when
        var api = _sut.GetMonitoringApi();

        // then
        api.Should().NotBeNull();
        api.Should().BeOfType<InMemoryMonitoringApi>();
    }

    [Fact]
    public async Task should_be_thread_safe_for_concurrent_writes()
    {
        // given
        var messageCount = 100;
        var messages = Enumerable.Range(0, messageCount)
            .Select(i => _CreateMessage($"concurrent-{i}"))
            .ToList();

        // when
        await Parallel.ForEachAsync(
            messages,
            new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (msg, _) => { await _sut.StoreMessageAsync("topic", msg, cancellationToken: AbortToken); }
        );

        // then
        InMemoryDataStorage.PublishedMessages.Should().HaveCount(messageCount);
    }

    private static Message _CreateMessage(string id)
    {
        return new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal) { { Headers.MessageId, id } },
            new { Data = "test" }
        );
    }
}
