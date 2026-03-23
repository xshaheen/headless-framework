// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("InMemoryStorage")]
public sealed class InMemoryDataStorageTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ISerializer _serializer = Substitute.For<ISerializer>();
    private readonly ILongIdGenerator _idGenerator = Substitute.For<ILongIdGenerator>();
    private readonly IOptions<MessagingOptions> _options = Options.Create(
        new MessagingOptions { FailedRetryCount = 3 }
    );
    private readonly InMemoryDataStorage _sut;
    private long _idCounter = 1000;

    public InMemoryDataStorageTests()
    {
        // Clear static state before each test
        InMemoryDataStorage.PublishedMessages.Clear();
        InMemoryDataStorage.ReceivedMessages.Clear();
        InMemoryDataStorage.Locks.Clear();

        _timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
        _serializer.Serialize(Arg.Any<Message>()).Returns(call => JsonSerializer.Serialize(call.Arg<Message>()));
        _idGenerator.Create().Returns(_ => Interlocked.Increment(ref _idCounter));

        _sut = new InMemoryDataStorage(_options, _serializer, _idGenerator, _timeProvider);
    }

    [Fact]
    public async Task should_store_published_message()
    {
        // given
        var message = _CreateMessage("1001");

        // when
        var result = await _sut.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);

        // then
        result.DbId.Should().Be("1001");
        result.Origin.Should().BeSameAs(message);
        result.Retries.Should().Be(0);
        result.Added.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);

        InMemoryDataStorage.PublishedMessages.Should().ContainKey("1001");
        InMemoryDataStorage.PublishedMessages["1001"].Name.Should().Be("test.topic");
        InMemoryDataStorage.PublishedMessages["1001"].StatusName.Should().Be(StatusName.Scheduled);
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
        await _sut.StoreReceivedExceptionMessageAsync(
            "failed.topic",
            "error-group",
            content,
            cancellationToken: AbortToken
        );

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
        var message = _CreateMessage("1002");
        var stored = await _sut.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);

        // when
        await _sut.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // then
        InMemoryDataStorage.PublishedMessages["1002"].StatusName.Should().Be(StatusName.Succeeded);
        InMemoryDataStorage.PublishedMessages["1002"].ExpiresAt.Should().Be(stored.ExpiresAt);
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
        await _sut.StoreMessageAsync("topic", _CreateMessage("1003"), cancellationToken: AbortToken);
        await _sut.StoreMessageAsync("topic", _CreateMessage("1004"), cancellationToken: AbortToken);

        // when
        await _sut.ChangePublishStateToDelayedAsync(["1003", "1004"], AbortToken);

        // then
        InMemoryDataStorage.PublishedMessages["1003"].StatusName.Should().Be(StatusName.Delayed);
        InMemoryDataStorage.PublishedMessages["1004"].StatusName.Should().Be(StatusName.Delayed);
    }

    [Fact]
    public async Task should_not_acquire_lock_when_already_held()
    {
        // when
        var firstLock = await _sut.AcquireLockAsync("test-lock", TimeSpan.FromMinutes(5), "instance-1", AbortToken);
        var secondLock = await _sut.AcquireLockAsync("test-lock", TimeSpan.FromMinutes(5), "instance-2", AbortToken);

        // then
        firstLock.Should().BeTrue();
        secondLock.Should().BeFalse();
    }

    [Fact]
    public async Task should_release_lock_for_matching_instance()
    {
        // given
        await _sut.AcquireLockAsync("test-lock", TimeSpan.FromMinutes(5), "instance-1", AbortToken);

        // when
        await _sut.ReleaseLockAsync("test-lock", "instance-1", AbortToken);

        // then
        var reacquired = await _sut.AcquireLockAsync("test-lock", TimeSpan.FromMinutes(5), "instance-2", AbortToken);
        reacquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_renew_lock_extends_ttl()
    {
        // given
        await _sut.AcquireLockAsync("test-lock", TimeSpan.FromMinutes(5), "instance-1", AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        // when
        await _sut.RenewLockAsync("test-lock", TimeSpan.FromMinutes(5), "instance-1", AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(4));

        // then
        var acquired = await _sut.AcquireLockAsync("test-lock", TimeSpan.FromMinutes(5), "instance-2", AbortToken);
        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_non_numeric_published_message_id()
    {
        // when
        var act = async () => await _sut.StoreMessageAsync("topic", _CreateMessage("msg-1"), cancellationToken: AbortToken);

        // then
        await act
            .Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("Published message IDs must be numeric*");
    }

    [Fact]
    public async Task should_get_published_messages_for_retry()
    {
        // given
        _timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var msg1 = await _sut.StoreMessageAsync("topic", _CreateMessage("1005"), cancellationToken: AbortToken);
        var msg2 = await _sut.StoreMessageAsync("topic", _CreateMessage("1006"), cancellationToken: AbortToken);

        // Set one to failed status
        await _sut.ChangePublishStateAsync(msg2, StatusName.Failed, cancellationToken: AbortToken);

        // Advance time past lookback window
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var result = await _sut.GetPublishedMessagesOfNeedRetry(TimeSpan.FromMinutes(1), AbortToken);

        // then
        var messages = result.ToList();
        messages.Should().HaveCount(2);
        messages.Should().Contain(m => m.DbId == "1005");
        messages.Should().Contain(m => m.DbId == "1006");
    }

    [Fact]
    public async Task should_not_return_messages_with_max_retries_exceeded()
    {
        // given
        _timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
        await _sut.StoreMessageAsync("topic", _CreateMessage("1007"), cancellationToken: AbortToken);

        // Set retries to max
        InMemoryDataStorage.PublishedMessages["1007"].Retries = _options.Value.FailedRetryCount;

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
        var expired = await _sut.StoreMessageAsync("topic", _CreateMessage("1008"), cancellationToken: AbortToken);
        var notExpired = await _sut.StoreMessageAsync("topic", _CreateMessage("1009"), cancellationToken: AbortToken);
        await _sut.ChangePublishStateAsync(expired, StatusName.Failed, cancellationToken: AbortToken);
        await _sut.ChangePublishStateAsync(notExpired, StatusName.Succeeded, cancellationToken: AbortToken);

        InMemoryDataStorage.PublishedMessages["1008"].ExpiresAt = _timeProvider
            .GetUtcNow()
            .UtcDateTime.AddHours(-1);
        InMemoryDataStorage.PublishedMessages["1009"].ExpiresAt = _timeProvider
            .GetUtcNow()
            .UtcDateTime.AddHours(1);

        // when
        var deleted = await _sut.DeleteExpiresAsync(
            nameof(InMemoryDataStorage.PublishedMessages),
            _timeProvider.GetUtcNow().UtcDateTime,
            batchCount: 100,
            cancellationToken: AbortToken
        );

        // then
        deleted.Should().Be(1);
        InMemoryDataStorage.PublishedMessages.Should().NotContainKey("1008");
        InMemoryDataStorage.PublishedMessages.Should().ContainKey("1009");
    }

    [Fact]
    public async Task should_delete_expired_received_messages()
    {
        // given
        var msg1 = await _sut.StoreReceivedMessageAsync("topic", "group", _CreateMessage("recv-expired"), AbortToken);
        var msg2 = await _sut.StoreReceivedMessageAsync(
            "topic",
            "group",
            _CreateMessage("recv-not-expired"),
            AbortToken
        );
        await _sut.ChangeReceiveStateAsync(msg1, StatusName.Failed, AbortToken);
        await _sut.ChangeReceiveStateAsync(msg2, StatusName.Succeeded, AbortToken);

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
            var msg = await _sut.StoreMessageAsync(
                "topic",
                _CreateMessage((1100 + i).ToString(CultureInfo.InvariantCulture)),
                cancellationToken: AbortToken
            );
            await _sut.ChangePublishStateAsync(msg, StatusName.Failed, cancellationToken: AbortToken);
            InMemoryDataStorage.PublishedMessages[msg.DbId].ExpiresAt = _timeProvider
                .GetUtcNow()
                .UtcDateTime.AddHours(-1);
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
    public async Task should_not_delete_expired_non_terminal_messages()
    {
        // given
        var delayed = await _sut.StoreMessageAsync("topic", _CreateMessage("1111"), cancellationToken: AbortToken);
        await _sut.ChangePublishStateAsync(delayed, StatusName.Delayed, cancellationToken: AbortToken);
        InMemoryDataStorage.PublishedMessages["1111"].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);

        // when
        var deleted = await _sut.DeleteExpiresAsync(
            nameof(InMemoryDataStorage.PublishedMessages),
            _timeProvider.GetUtcNow().UtcDateTime,
            batchCount: 100,
            cancellationToken: AbortToken
        );

        // then
        deleted.Should().Be(0);
        InMemoryDataStorage.PublishedMessages.Should().ContainKey("1111");
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
        var msg = await _sut.StoreMessageAsync("topic", _CreateMessage("1010"), cancellationToken: AbortToken);
        await _sut.ChangePublishStateAsync(msg, StatusName.Delayed, cancellationToken: AbortToken);
        InMemoryDataStorage.PublishedMessages["1010"].ExpiresAt = _timeProvider
            .GetUtcNow()
            .UtcDateTime.AddMinutes(1);

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
        scheduledMessages.Should().ContainSingle().Which.DbId.Should().Be("1010");
    }

    [Fact]
    public async Task should_schedule_queued_messages_past_expiry()
    {
        // given
        var msg = await _sut.StoreMessageAsync("topic", _CreateMessage("1011"), cancellationToken: AbortToken);
        await _sut.ChangePublishStateAsync(msg, StatusName.Queued, cancellationToken: AbortToken);
        InMemoryDataStorage.PublishedMessages["1011"].ExpiresAt = _timeProvider
            .GetUtcNow()
            .UtcDateTime.AddMinutes(-2);

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
        scheduledMessages.Should().ContainSingle().Which.DbId.Should().Be("1011");
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
        var messages = Enumerable
            .Range(0, messageCount)
            .Select(i => _CreateMessage((2000 + i).ToString(CultureInfo.InvariantCulture)))
            .ToList();

        // when
        await Parallel.ForEachAsync(
            messages,
            new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (msg, _) =>
            {
                await _sut.StoreMessageAsync("topic", msg, cancellationToken: AbortToken);
            }
        );

        // then
        InMemoryDataStorage.PublishedMessages.Should().HaveCount(messageCount);
    }

    private static Message _CreateMessage(string id)
    {
        return new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal) { { MessagingHeaders.MessageId, id } },
            new { Data = "test" }
        );
    }
}
