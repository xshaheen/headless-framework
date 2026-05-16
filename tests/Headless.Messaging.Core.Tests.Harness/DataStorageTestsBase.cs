// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Tests.Capabilities;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>Base class for data storage implementation tests.</summary>
[PublicAPI]
public abstract class DataStorageTestsBase : TestBase
{
    /// <summary>Gets the data storage instance for testing.</summary>
    protected abstract IDataStorage GetStorage();

    /// <summary>Gets the storage initializer instance for testing.</summary>
    protected abstract IStorageInitializer GetInitializer();

    /// <summary>Gets the serializer for creating message content.</summary>
    protected abstract ISerializer GetSerializer();

    /// <summary>Gets the data storage capabilities for conditional test execution.</summary>
    protected virtual DataStorageCapabilities Capabilities => DataStorageCapabilities.Default;

    /// <summary>
    /// Thread-safe counter for generating unique logical message IDs.
    /// </summary>
    private static long _MessageIdCounter;

    /// <summary>Creates a valid message for testing.</summary>
    protected static Message CreateMessage(string? messageId = null, string? messageName = null, object? value = null)
    {
        var id = messageId ?? $"msg-{Interlocked.Increment(ref _MessageIdCounter)}";

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, id },
            { MessagingHeaders.MessageName, messageName ?? "TestMessage" },
            { MessagingHeaders.SentTime, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
        };

        return new Message(headers, value ?? new { Data = "test" });
    }

    public virtual async Task should_initialize_schema()
    {
        // given
        var initializer = GetInitializer();

        // when
        var act = async () => await initializer.InitializeAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual Task should_get_table_names()
    {
        // given
        var initializer = GetInitializer();

        // when
        var publishedTable = initializer.GetPublishedTableName();
        var receivedTable = initializer.GetReceivedTableName();
        var lockTable = initializer.GetLockTableName();

        // then
        publishedTable.Should().NotBeNullOrEmpty();
        receivedTable.Should().NotBeNullOrEmpty();
        lockTable.Should().NotBeNullOrEmpty();

        return Task.CompletedTask;
    }

    public virtual async Task should_store_published_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        const string messageName = "test-published-message";

        // when
        var result = await storage.StoreMessageAsync(messageName, message, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result.StorageId.Should().BeGreaterThan(0);
        result.Origin.Should().BeSameAs(message);
    }

    public virtual async Task should_store_published_message_with_non_numeric_message_id()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage("non-numeric-id");

        // when
        var result = await storage.StoreMessageAsync("test-published-message", message, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result.StorageId.Should().BeGreaterThan(0);
        result.Origin.GetId().Should().Be("non-numeric-id");
    }

    public virtual async Task should_store_received_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        const string messageName = "test-received-message";
        const string group = "test-group";

        // when
        var result = await storage.StoreReceivedMessageAsync(messageName, group, message, AbortToken);

        // then
        result.Should().NotBeNull();
        result.StorageId.Should().BeGreaterThan(0);
        result.Origin.Should().BeSameAs(message);
    }

    public virtual async Task should_store_received_exception_message()
    {
        // given
        var storage = GetStorage();
        var serializer = GetSerializer();
        const string messageName = "exception-message";
        const string group = "test-group";
        // StoreReceivedExceptionMessageAsync expects serialized Message JSON with headers, not raw text
        var message = CreateMessage();
        var content = serializer.Serialize(message);

        // when
        var act = async () =>
            await storage.StoreReceivedExceptionMessageAsync(
                messageName,
                group,
                content,
                cancellationToken: AbortToken
            );

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_change_publish_state()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("state-test", message, cancellationToken: AbortToken);

        // when
        var act = async () =>
            await storage.ChangePublishStateAsync(storedMessage, StatusName.Succeeded, cancellationToken: AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_change_receive_state()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync("state-test", "group", message, AbortToken);

        // when
        var act = async () =>
            await storage.ChangeReceiveStateAsync(storedMessage, StatusName.Succeeded, cancellationToken: AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_change_publish_state_to_delayed()
    {
        // Skip if storage doesn't support delayed scheduling
        if (!Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling");
        }

        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("delayed-test", message, cancellationToken: AbortToken);

        // when
        var act = async () => await storage.ChangePublishStateToDelayedAsync([storedMessage.StorageId], AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_get_published_messages_of_need_retry()
    {
        // given
        var storage = GetStorage();
        // when
        var result = await storage.GetPublishedMessagesOfNeedRetry(AbortToken);

        // then
        result.Should().NotBeNull();
    }

    public virtual async Task should_get_received_messages_of_need_retry()
    {
        // given
        var storage = GetStorage();
        // when
        var result = await storage.GetReceivedMessagesOfNeedRetry(AbortToken);

        // then
        result.Should().NotBeNull();
    }

    public virtual async Task should_acquire_lock()
    {
        // Skip if storage doesn't support locking
        if (!Capabilities.SupportsLocking)
        {
            Assert.Skip("Storage does not support locking");
        }

        // given
        var storage = GetStorage();
        const string lockKey = "publish_retry_v1";
        var instanceId = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromSeconds(30);

        // when
        var result = await storage.AcquireLockAsync(lockKey, ttl, instanceId, AbortToken);

        // then
        result.Should().BeTrue();

        // cleanup
        await storage.ReleaseLockAsync(lockKey, instanceId, AbortToken);
    }

    public virtual async Task should_not_acquire_lock_when_already_held()
    {
        // Skip if storage doesn't support locking
        if (!Capabilities.SupportsLocking)
        {
            Assert.Skip("Storage does not support locking");
        }

        // given
        var storage = GetStorage();
        const string lockKey = "publish_retry_v1";
        var instanceId1 = Guid.NewGuid().ToString();
        var instanceId2 = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromSeconds(30);

        // when
        var firstLock = await storage.AcquireLockAsync(lockKey, ttl, instanceId1, AbortToken);
        var secondLock = await storage.AcquireLockAsync(lockKey, ttl, instanceId2, AbortToken);

        // then
        firstLock.Should().BeTrue();
        secondLock.Should().BeFalse();

        // cleanup
        await storage.ReleaseLockAsync(lockKey, instanceId1, AbortToken);
    }

    public virtual async Task should_release_lock()
    {
        // Skip if storage doesn't support locking
        if (!Capabilities.SupportsLocking)
        {
            Assert.Skip("Storage does not support locking");
        }

        // given
        var storage = GetStorage();
        const string lockKey = "publish_retry_v1";
        var instanceId = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromSeconds(30);

        await storage.AcquireLockAsync(lockKey, ttl, instanceId, AbortToken);

        // when
        var act = async () => await storage.ReleaseLockAsync(lockKey, instanceId, AbortToken);

        // then
        await act.Should().NotThrowAsync();

        // Verify lock can be acquired again
        var canAcquireAgain = await storage.AcquireLockAsync(lockKey, ttl, instanceId, AbortToken);
        canAcquireAgain.Should().BeTrue();

        // cleanup
        await storage.ReleaseLockAsync(lockKey, instanceId, AbortToken);
    }

    public virtual async Task should_renew_lock()
    {
        // Skip if storage doesn't support locking
        if (!Capabilities.SupportsLocking)
        {
            Assert.Skip("Storage does not support locking");
        }

        // given
        var storage = GetStorage();
        const string lockKey = "publish_retry_v1";
        var instanceId = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromSeconds(30);

        await storage.AcquireLockAsync(lockKey, ttl, instanceId, AbortToken);

        // when
        var act = async () => await storage.RenewLockAsync(lockKey, ttl, instanceId, AbortToken);

        // then
        await act.Should().NotThrowAsync();

        // cleanup
        await storage.ReleaseLockAsync(lockKey, instanceId, AbortToken);
    }

    public virtual async Task should_delete_expired_messages()
    {
        // Skip if storage doesn't support expiration
        if (!Capabilities.SupportsExpiration)
        {
            Assert.Skip("Storage does not support message expiration");
        }

        // given
        var storage = GetStorage();
        var initializer = GetInitializer();
        var tableName = initializer.GetPublishedTableName();
        var timeout = DateTime.UtcNow.AddMinutes(-10);

        // when
        var deletedCount = await storage.DeleteExpiresAsync(tableName, timeout, 100, AbortToken);

        // then
        deletedCount.Should().BeGreaterThanOrEqualTo(0);
    }

    public virtual async Task should_delete_published_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("delete-test", message, cancellationToken: AbortToken);

        // when
        var deletedCount = await storage.DeletePublishedMessageAsync(storedMessage.StorageId, AbortToken);

        // then
        deletedCount.Should().Be(1);
    }

    public virtual async Task should_delete_received_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync("delete-test", "group", message, AbortToken);

        // when
        var deletedCount = await storage.DeleteReceivedMessageAsync(storedMessage.StorageId, AbortToken);

        // then
        deletedCount.Should().Be(1);
    }

    public virtual Task should_get_monitoring_api()
    {
        // Skip if storage doesn't support monitoring API
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support monitoring API");
        }

        // given
        var storage = GetStorage();

        // when
        var monitoringApi = storage.GetMonitoringApi();

        // then
        monitoringApi.Should().NotBeNull();
        return Task.CompletedTask;
    }

    public virtual async Task should_handle_concurrent_storage_operations()
    {
        // Skip if storage doesn't support concurrent operations
        if (!Capabilities.SupportsConcurrentOperations)
        {
            Assert.Skip("Storage does not support concurrent operations");
        }

        // given
        var storage = GetStorage();
        var results = new ConcurrentBag<MediumMessage>();

        // when
        var tasks = Enumerable
            .Range(0, 20)
            .Select(async i =>
            {
                var message = CreateMessage();
                var result = await storage.StoreMessageAsync(
                    $"concurrent-topic-{i}",
                    message,
                    cancellationToken: AbortToken
                );
                results.Add(result);
            });

        await Task.WhenAll(tasks);

        // then
        results.Should().HaveCount(20);
        results.Should().AllSatisfy(r => r.StorageId.Should().BeGreaterThan(0));
    }

    public virtual async Task should_schedule_messages_of_delayed()
    {
        // Skip if storage doesn't support delayed scheduling
        if (!Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling");
        }

        // given
        var storage = GetStorage();
        var scheduledMessages = new List<MediumMessage>();

        // when
        await storage.ScheduleMessagesOfDelayedAsync(
            (_, messages) =>
            {
                scheduledMessages.AddRange(messages);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );

        // then - should complete without exception
        scheduledMessages.Should().NotBeNull();
    }

    public virtual async Task should_store_message_with_transaction()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();

        // when — null transaction path (provider-specific transaction tests should cover the real path)
        var result = await storage.StoreMessageAsync("transaction-test", message, transaction: null, AbortToken);

        // then
        result.Should().NotBeNull();
        result.StorageId.Should().BeGreaterThan(0);
        result.Origin.Should().BeSameAs(message);
        result.Retries.Should().Be(0);
    }

    public virtual async Task should_handle_message_state_transitions()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("state-transition", message, cancellationToken: AbortToken);

        // when - transition through states
        await storage.ChangePublishStateAsync(storedMessage, StatusName.Scheduled, cancellationToken: AbortToken);
        await storage.ChangePublishStateAsync(storedMessage, StatusName.Succeeded, cancellationToken: AbortToken);

        // then - no exception thrown
        storedMessage.Should().NotBeNull();
    }

    public virtual async Task should_handle_failed_message_state()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("failed-state", message, cancellationToken: AbortToken);

        // when
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // then — the failed message should appear in retry results once its scheduled retry time is due.
        var retriable = await storage.GetPublishedMessagesOfNeedRetry(AbortToken);
        retriable.Should().NotBeNull();
        retriable.Should().Contain(m => m.StorageId == storedMessage.StorageId);
    }

    // -------------------------------------------------------------------------
    // Negative NextRetryAt filter cases — Failed messages must not be picked up
    // unless NextRetryAt is in the past. Mirrors the partial-index predicates.
    // -------------------------------------------------------------------------

    public virtual async Task should_not_return_published_message_with_failed_status_and_null_next_retry_at()
    {
        // given — a Failed message with NextRetryAt = NULL represents a permanent failure
        // (Stop classification). It must NOT be returned by the retry-pickup query.
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync(
            "failed-null-next-retry",
            message,
            cancellationToken: AbortToken
        );

        // when
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: null,
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetPublishedMessagesOfNeedRetry(AbortToken);
        retriable.Should().NotBeNull();
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_published_message_with_future_next_retry_at()
    {
        // given — a Failed message scheduled for the future must NOT be returned until its
        // retry time is due (the query predicate is NextRetryAt <= now()).
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync(
            "failed-future-next-retry",
            message,
            cancellationToken: AbortToken
        );

        // when
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddHours(1),
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetPublishedMessagesOfNeedRetry(AbortToken);
        retriable.Should().NotBeNull();
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_received_message_with_failed_status_and_null_next_retry_at()
    {
        // given — a Failed received message with NextRetryAt = NULL must NOT be returned.
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "failed-null-next-retry",
            "test-group",
            message,
            AbortToken
        );

        // when
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: null,
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetReceivedMessagesOfNeedRetry(AbortToken);
        retriable.Should().NotBeNull();
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_received_message_with_future_next_retry_at()
    {
        // given — a Failed received message scheduled for the future must NOT be returned
        // until its retry time is due.
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "failed-future-next-retry",
            "test-group",
            message,
            AbortToken
        );

        // when
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddHours(1),
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetReceivedMessagesOfNeedRetry(AbortToken);
        retriable.Should().NotBeNull();
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_pickup_message_at_max_persisted_retries_and_exclude_above()
    {
        // given — with MaxPersistedRetries = 4, the pickup predicate is `Retries <= 4`.
        // Retries == 4 is the LAST allowed pickup (where the helper returns Exhausted on
        // budget consumption). Retries == 5 represents the terminal state past the budget
        // and must NOT be picked up. Total dispatches = (MaxPersistedRetries + 1) = 5.
        var storage = GetStorage();
        var message = CreateMessage();

        // Boundary case 1 (published): Retries == MaxPersistedRetries → picked up.
        var atLimit = await storage.StoreMessageAsync("max-retries-test-pub", message, cancellationToken: AbortToken);
        atLimit.Retries = 4;
        await storage.ChangePublishStateAsync(
            atLimit,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // Boundary case 2 (published): Retries == MaxPersistedRetries + 1 → NOT picked up.
        var aboveLimit = await storage.StoreMessageAsync(
            "above-retries-test-pub",
            message,
            cancellationToken: AbortToken
        );
        aboveLimit.Retries = 5;
        await storage.ChangePublishStateAsync(
            aboveLimit,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // when
        var retriable = (await storage.GetPublishedMessagesOfNeedRetry(AbortToken)).ToList();

        // then
        retriable.Should().Contain(m => m.StorageId == atLimit.StorageId);
        retriable.Should().NotContain(m => m.StorageId == aboveLimit.StorageId);

        // Same boundary semantics for received messages.
        var atLimitRecv = await storage.StoreReceivedMessageAsync(
            "max-retries-test-recv",
            "group",
            message,
            AbortToken
        );
        atLimitRecv.Retries = 4;
        await storage.ChangeReceiveStateAsync(
            atLimitRecv,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var aboveLimitRecv = await storage.StoreReceivedMessageAsync(
            "above-retries-test-recv",
            "group",
            message,
            AbortToken
        );
        aboveLimitRecv.Retries = 5;
        await storage.ChangeReceiveStateAsync(
            aboveLimitRecv,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var retriableReceived = (await storage.GetReceivedMessagesOfNeedRetry(AbortToken)).ToList();
        retriableReceived.Should().Contain(m => m.StorageId == atLimitRecv.StorageId);
        retriableReceived.Should().NotContain(m => m.StorageId == aboveLimitRecv.StorageId);
    }
}
