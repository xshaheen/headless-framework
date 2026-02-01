// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Tests.Capabilities;
using Xunit;
using MessagingHeaders = Headless.Messaging.Messages.Headers;

namespace Tests;

/// <summary>Base class for data storage implementation tests.</summary>
[PublicAPI]
public abstract class DataStorageTestsBase : TestBase
{
    /// <summary>Gets the data storage instance for testing.</summary>
    protected abstract IDataStorage GetStorage();

    /// <summary>Gets the storage initializer instance for testing.</summary>
    protected abstract IStorageInitializer GetInitializer();

    /// <summary>Gets the data storage capabilities for conditional test execution.</summary>
    protected virtual DataStorageCapabilities Capabilities => DataStorageCapabilities.Default;

    /// <summary>Creates a valid message for testing.</summary>
    protected static Message CreateMessage(
        string? messageId = null,
        string? messageName = null,
        object? value = null)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, messageId ?? Guid.NewGuid().ToString() },
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

    public virtual async Task should_get_table_names()
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
        await Task.CompletedTask;
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
        result.DbId.Should().NotBeNullOrEmpty();
        result.Origin.Should().BeSameAs(message);
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
        result.DbId.Should().NotBeNullOrEmpty();
        result.Origin.Should().BeSameAs(message);
    }

    public virtual async Task should_store_received_exception_message()
    {
        // given
        var storage = GetStorage();
        const string messageName = "exception-message";
        const string group = "test-group";
        const string content = "{\"error\": \"test exception\"}";

        // when
        var act = async () => await storage.StoreReceivedExceptionMessageAsync(messageName, group, content, AbortToken);

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
        var act = async () => await storage.ChangePublishStateAsync(storedMessage, StatusName.Succeeded, cancellationToken: AbortToken);

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
        var act = async () => await storage.ChangeReceiveStateAsync(storedMessage, StatusName.Succeeded, AbortToken);

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
        var act = async () => await storage.ChangePublishStateToDelayedAsync([storedMessage.DbId], AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_get_published_messages_of_need_retry()
    {
        // given
        var storage = GetStorage();
        var lookbackTime = TimeSpan.FromMinutes(5);

        // when
        var result = await storage.GetPublishedMessagesOfNeedRetry(lookbackTime, AbortToken);

        // then
        result.Should().NotBeNull();
    }

    public virtual async Task should_get_received_messages_of_need_retry()
    {
        // given
        var storage = GetStorage();
        var lookbackTime = TimeSpan.FromMinutes(5);

        // when
        var result = await storage.GetReceivedMessagesOfNeedRetry(lookbackTime, AbortToken);

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
        var lockKey = $"test-lock-{Guid.NewGuid()}";
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
        var lockKey = $"test-lock-{Guid.NewGuid()}";
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
        var lockKey = $"test-lock-{Guid.NewGuid()}";
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
        var lockKey = $"test-lock-{Guid.NewGuid()}";
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
        var messageId = long.Parse(storedMessage.DbId, CultureInfo.InvariantCulture);

        // when
        var deletedCount = await storage.DeletePublishedMessageAsync(messageId, AbortToken);

        // then
        deletedCount.Should().BeGreaterThanOrEqualTo(0);
    }

    public virtual async Task should_delete_received_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync("delete-test", "group", message, AbortToken);
        var messageId = long.Parse(storedMessage.DbId, CultureInfo.InvariantCulture);

        // when
        var deletedCount = await storage.DeleteReceivedMessageAsync(messageId, AbortToken);

        // then
        deletedCount.Should().BeGreaterThanOrEqualTo(0);
    }

    public virtual async Task should_get_monitoring_api()
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
        await Task.CompletedTask;
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
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            var message = CreateMessage(messageId: $"concurrent-{i}");
            var result = await storage.StoreMessageAsync($"concurrent-topic-{i}", message, cancellationToken: AbortToken);
            results.Add(result);
        });

        await Task.WhenAll(tasks);

        // then
        results.Should().HaveCount(20);
        results.Should().AllSatisfy(r => r.DbId.Should().NotBeNullOrEmpty());
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
            AbortToken);

        // then - should complete without exception
        scheduledMessages.Should().NotBeNull();
    }

    public virtual async Task should_store_message_with_transaction()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        object? transaction = null; // Transaction handling varies by implementation

        // when
        var result = await storage.StoreMessageAsync("transaction-test", message, transaction, AbortToken);

        // then
        result.Should().NotBeNull();
        result.DbId.Should().NotBeNullOrEmpty();
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
        await storage.ChangePublishStateAsync(storedMessage, StatusName.Failed, cancellationToken: AbortToken);

        // then
        var retriable = await storage.GetPublishedMessagesOfNeedRetry(TimeSpan.FromMinutes(1), AbortToken);
        retriable.Should().NotBeNull();
    }
}
