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
    /// Counts persisted received-message rows matching the supplied <paramref name="messageId"/>
    /// (and optionally <paramref name="group"/>). Provider-specific because the row visibility
    /// after a concurrent upsert storm needs a direct count query — the public monitoring API
    /// does not filter by MessageId.
    /// </summary>
    protected abstract Task<int> CountReceivedMessagesByIdentityAsync(
        string messageId,
        string? group,
        CancellationToken cancellationToken
    );

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
        var result = await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken);

        // then
        result.Should().NotBeNull();
    }

    public virtual async Task should_get_received_messages_of_need_retry()
    {
        // given
        var storage = GetStorage();
        // when
        var result = await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);

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
        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken);
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
        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken);
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
        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken);
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
        var retriable = await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);
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
        var retriable = await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);
        retriable.Should().NotBeNull();
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_leased_published_message_until_lease_expires()
    {
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "leased-published",
            CreateMessage(),
            cancellationToken: AbortToken
        );

        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var leased = await storage.LeasePublishAsync(storedMessage, DateTime.UtcNow.AddMinutes(5), AbortToken);

        leased.Should().BeTrue();
        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        await storage.LeasePublishAsync(storedMessage, DateTime.UtcNow.AddSeconds(-1), AbortToken);

        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_leased_received_message_until_lease_expires()
    {
        // Asymmetric coverage parity with the published-lease test above. Mirrors the receive
        // lease semantics: Failed/NextRetryAt-in-past row with a future LockedUntil is excluded
        // from the retry pickup until LockedUntil expires.
        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "leased-received",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var leased = await storage.LeaseReceiveAsync(storedMessage, DateTime.UtcNow.AddMinutes(5), AbortToken);

        leased.Should().BeTrue();
        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        await storage.LeaseReceiveAsync(storedMessage, DateTime.UtcNow.AddSeconds(-1), AbortToken);

        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_handle_concurrent_state_updates_to_same_row()
    {
        // Concurrent CAS / optimistic-concurrency contract: exactly one of N parallel
        // ChangeReceiveStateAsync calls with originalRetries=0 must succeed. The others must
        // return false because Retries no longer equals the original value (or because the row
        // is now terminal). Validates the per-row CAS guard used to prevent inverse-order pickups
        // from overwriting each other's terminal writes.
        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "concurrent-cas",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        // Transition to Failed/NextRetryAt-in-future so the row stays mutable (terminal guard
        // would otherwise reject EVERY call regardless of originalRetries semantics).
        storedMessage.Retries = 0;
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddMinutes(5),
            cancellationToken: AbortToken
        );

        const int concurrency = 20;
        var bag = new ConcurrentBag<bool>();
        await Task.WhenAll(
            Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        var localCopy = new MediumMessage
                        {
                            StorageId = storedMessage.StorageId,
                            Origin = storedMessage.Origin,
                            Content = storedMessage.Content,
                            Retries = 1,
                        };
                        var ok = await storage.ChangeReceiveStateAsync(
                            localCopy,
                            StatusName.Failed,
                            nextRetryAt: DateTime.UtcNow.AddMinutes(10),
                            originalRetries: 0,
                            cancellationToken: AbortToken
                        );
                        bag.Add(ok);
                    })
                )
        );

        bag.Count(x => x).Should().Be(1, "exactly one concurrent CAS update must win");
        bag.Count(x => !x).Should().Be(concurrency - 1, "all other writers must observe stale Retries");
    }

    public virtual async Task should_reject_mismatched_original_retries()
    {
        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "retry-race",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        storedMessage.Retries = 1;
        var first = await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            originalRetries: 0,
            cancellationToken: AbortToken
        );

        var second = await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow.AddSeconds(-1),
            originalRetries: 0,
            cancellationToken: AbortToken
        );

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    public virtual async Task should_report_false_when_received_exception_message_is_already_terminal()
    {
        var storage = GetStorage();
        var serializer = GetSerializer();
        var message = CreateMessage();
        var content = serializer.Serialize(message);

        var first = await storage.StoreReceivedExceptionMessageAsync(
            "poisoned",
            "test-group",
            content,
            "first",
            AbortToken
        );
        var second = await storage.StoreReceivedExceptionMessageAsync(
            "poisoned",
            "test-group",
            content,
            "second",
            AbortToken
        );

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    public virtual async Task should_handle_concurrent_redelivery_storm_on_same_message_id()
    {
        // Fan-out concurrency test for StoreReceivedExceptionMessageAsync's upsert identity
        // (Version, MessageId, Group). N parallel writers for the SAME message must collapse
        // to exactly one row. The last writer's exceptionInfo must win (the upsert is an
        // unconditional overwrite when the existing row is non-terminal — the terminal-row
        // guard only blocks updates against Succeeded/Failed-NULL rows; the first call here
        // creates a Failed/NULL row, so subsequent storm writes hit the terminal guard and
        // return false. We assert the row-count invariant and that exception info matches one
        // of the contributors — see the comment in the InMemory provider on upsert semantics).
        // R10 — pre-warm the thread pool so a CI box with a low default min-thread count does not
        // starve the workers and produce a false-failure "lock starvation suspected" within the
        // 30s wall-clock budget. Without this, 64 Task.Run callbacks can sit in the global queue
        // for seconds before the threadpool grows.
        ThreadPool.SetMinThreads(
            Math.Max(64, Environment.ProcessorCount * 2),
            Math.Max(64, Environment.ProcessorCount * 2)
        );

        var storage = GetStorage();
        var serializer = GetSerializer();
        var message = CreateMessage($"storm-{Guid.NewGuid():N}");
        var content = serializer.Serialize(message);
        const string group = "storm-group";
        const int concurrency = 64;

        using var startGate = new ManualResetEventSlim(false);
        var results = new ConcurrentBag<bool>();

        var workers = Enumerable
            .Range(0, concurrency)
            .Select(index =>
                Task.Run(async () =>
                {
                    startGate.Wait(AbortToken);
                    var ok = await storage.StoreReceivedExceptionMessageAsync(
                        "redelivery-storm",
                        group,
                        content,
                        $"writer-{index}",
                        AbortToken
                    );
                    results.Add(ok);
                })
            )
            .ToArray();

        // Release every worker at the same instant so the test exercises the contended path,
        // not the trivial sequential one.
        startGate.Set();

        // Hard timeout — if the secondary-index path regresses to an O(N) scan inside an
        // exclusive lock the storm would either deadlock or take orders of magnitude longer
        // than 30 seconds. Failing fast surfaces the regression.
        var stormCompletion = Task.WhenAll(workers);
        var timeout = Task.Delay(TimeSpan.FromSeconds(30), AbortToken);
        var winner = await Task.WhenAny(stormCompletion, timeout);
        winner
            .Should()
            .BeSameAs(stormCompletion, "storm must finish well under 30s — lock starvation suspected otherwise");

        // Exactly one writer should report true (the inserter); all others observe the existing
        // terminal-NULL row and return false. The single-row invariant is then verified via a
        // direct identity probe — calling StoreReceivedExceptionMessageAsync again with new
        // exception info must also return false because the row is now terminal.
        results.Count(x => x).Should().Be(1, "exactly one concurrent upsert must insert the row");
        results.Count(x => !x).Should().Be(concurrency - 1, "all losers must observe the existing terminal row");

        var followUp = await storage.StoreReceivedExceptionMessageAsync(
            "redelivery-storm",
            group,
            content,
            "post-storm",
            AbortToken
        );
        followUp.Should().BeFalse("the row is terminal — no further upserts should succeed");
    }

    public virtual async Task should_handle_concurrent_first_insert_storm_with_null_and_non_null_group()
    {
        // R1 regression — guards against the F8-redux duplicate-row bug on the
        // StoreReceivedExceptionMessageAsync path. Two parallel storms exercise both halves of the
        // upsert identity:
        //   - NULL Group: a plain ("MessageId","Group") unique index treats NULLs as distinct, so
        //     the previous SELECT-FOR-UPDATE-then-INSERT pattern let two concurrent first-inserts
        //     both fall through and produce duplicate rows. PostgreSQL must rely on a NULL-safe
        //     conflict target (COALESCE("Group", '')); SqlServer's MERGE already handles this
        //     case in its ON clause.
        //   - non-NULL Group: the standard unique-constraint path. A regression would either
        //     produce duplicates (no constraint at all) or surface a raw 23505 sqlstate /
        //     2627 unique-violation to the caller. We assert exactly one row converges and no
        //     exception escapes.
        // Pre-warm the thread pool so a CI box with low default min-threads does not starve the
        // workers and produce a false "lock starvation" timeout.
        ThreadPool.SetMinThreads(
            Math.Max(64, Environment.ProcessorCount * 2),
            Math.Max(64, Environment.ProcessorCount * 2)
        );

        var storage = GetStorage();
        var serializer = GetSerializer();
        const int concurrency = 32;

        await _RunFirstInsertStormAsync(group: null);
        await _RunFirstInsertStormAsync(group: "g1");

        return;

        async Task _RunFirstInsertStormAsync(string? group)
        {
            var messageId = $"first-insert-storm-{group ?? "null"}-{Guid.NewGuid():N}";
            var message = CreateMessage(messageId);
            var content = serializer.Serialize(message);

            using var startGate = new ManualResetEventSlim(initialState: false);
            var results = new ConcurrentBag<bool>();
            var exceptions = new ConcurrentBag<Exception>();

            var workers = Enumerable
                .Range(0, concurrency)
                .Select(index =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            startGate.Wait(AbortToken);
                            // group! tolerates the InMemory provider's non-nullable string group
                            // parameter while still exercising the SQL providers' COALESCE/MERGE
                            // NULL-equivalent upsert key on the database side.
                            var ok = await storage.StoreReceivedExceptionMessageAsync(
                                "first-insert-storm",
                                group!,
                                content,
                                $"writer-{index}",
                                AbortToken
                            );
                            results.Add(ok);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    })
                )
                .ToArray();

            startGate.Set();
            var stormCompletion = Task.WhenAll(workers);
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), AbortToken);
            var winner = await Task.WhenAny(stormCompletion, timeout);
            winner
                .Should()
                .BeSameAs(
                    stormCompletion,
                    $"storm with group={group ?? "<null>"} must finish well under 30s — lock starvation or deadlock suspected"
                );

            exceptions
                .Should()
                .BeEmpty(
                    $"no concurrent insert should surface a unique-violation or sqlstate 23505 to the caller (group={group ?? "<null>"})"
                );
            results.Count(x => x).Should().Be(1, $"exactly one writer must insert the row (group={group ?? "<null>"})");
            results
                .Count(x => !x)
                .Should()
                .Be(
                    concurrency - 1,
                    $"all losing writers must observe the existing terminal row (group={group ?? "<null>"})"
                );

            var rowCount = await CountReceivedMessagesByIdentityAsync(messageId, group, AbortToken);
            rowCount
                .Should()
                .Be(1, $"the concurrent storm must converge to exactly one persisted row (group={group ?? "<null>"})");
        }
    }

    public virtual async Task should_handle_concurrent_store_received_message_with_same_identity()
    {
        // R3 regression — StoreReceivedMessageAsync (the non-exception path) must also serialize
        // through the same identity check that StoreReceivedExceptionMessageAsync uses. Before R3
        // the InMemory provider's non-exception path performed an unconditional insert + index
        // overwrite, so two concurrent calls with the same (Version, MessageId, Group) produced
        // duplicate rows that both showed up in GetReceivedMessagesOfNeedRetryAsync — running the
        // consume executor twice. The SQL providers enforce uniqueness via the DB constraint;
        // this test exercises the InMemory parity path as well as the PG/SqlServer DB paths.
        ThreadPool.SetMinThreads(
            Math.Max(64, Environment.ProcessorCount * 2),
            Math.Max(64, Environment.ProcessorCount * 2)
        );

        var storage = GetStorage();
        const int concurrency = 32;

        await _RunStoreReceivedStormAsync(group: null);
        await _RunStoreReceivedStormAsync(group: "consume-group");

        return;

        async Task _RunStoreReceivedStormAsync(string? group)
        {
            var messageId = $"store-received-storm-{group ?? "null"}-{Guid.NewGuid():N}";
            var sharedMessage = CreateMessage(messageId);

            using var startGate = new ManualResetEventSlim(initialState: false);
            var exceptions = new ConcurrentBag<Exception>();

            var workers = Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            startGate.Wait(AbortToken);
                            await storage.StoreReceivedMessageAsync(
                                "store-received-storm",
                                group!,
                                sharedMessage,
                                AbortToken
                            );
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    })
                )
                .ToArray();

            startGate.Set();
            var stormCompletion = Task.WhenAll(workers);
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), AbortToken);
            var winner = await Task.WhenAny(stormCompletion, timeout);
            winner
                .Should()
                .BeSameAs(
                    stormCompletion,
                    $"storm with group={group ?? "<null>"} must finish well under 30s — lock starvation or deadlock suspected"
                );

            exceptions
                .Should()
                .BeEmpty(
                    $"no concurrent insert should surface a unique-violation or sqlstate 23505 to the caller (group={group ?? "<null>"})"
                );

            var rowCount = await CountReceivedMessagesByIdentityAsync(messageId, group, AbortToken);
            rowCount
                .Should()
                .Be(1, $"the concurrent storm must converge to exactly one persisted row (group={group ?? "<null>"})");
        }
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
        var retriable = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();

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

        var retriableReceived = (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken)).ToList();
        retriableReceived.Should().Contain(m => m.StorageId == atLimitRecv.StorageId);
        retriableReceived.Should().NotContain(m => m.StorageId == aboveLimitRecv.StorageId);
    }
}
