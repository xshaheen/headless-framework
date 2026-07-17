// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
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
    /// Gets the <see cref="TimeProvider"/> used by the storage under test. Defaults to
    /// <see cref="TimeProvider.System"/>; providers that want clock-controlled coverage of
    /// time-sensitive predicates override this with a <c>FakeTimeProvider</c> and rebuild their
    /// storage on top of it. SQL providers that depend on database-side time functions (e.g.,
    /// PostgreSQL's <c>now()</c> in the pickup query) skip the controllable-clock parity tests.
    /// </summary>
    protected virtual TimeProvider TimeProvider => TimeProvider.System;

    /// <summary>
    /// Indicates whether <see cref="TimeProvider"/> can be advanced under test. Providers backed
    /// by a <c>FakeTimeProvider</c> return <see langword="true"/>; default <see cref="TimeProvider.System"/>
    /// providers return <see langword="false"/> and the clock-controlled tests are skipped.
    /// </summary>
    protected virtual bool SupportsControllableClock => false;

    /// <summary>
    /// Creates another storage instance with the supplied application clock when the provider supports
    /// relational clock-skew conformance testing. Other providers return <see langword="null"/>.
    /// </summary>
    protected virtual IDataStorage? CreateStorageWithTimeProvider(TimeProvider timeProvider)
    {
        return null;
    }

    /// <summary>Overrides the dispatch timeout when the provider exposes mutable test options.</summary>
    protected virtual bool TrySetDispatchTimeout(TimeSpan dispatchTimeout)
    {
        return false;
    }

    /// <summary>Reads the provider's current database UTC time for relational clock conformance.</summary>
    protected virtual Task<DateTime?> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<DateTime?>(null);
    }

    /// <summary>Reads the persisted lease identity without going through the storage snapshot mapper.</summary>
    protected virtual Task<PersistedLeaseIdentity?> GetPersistedLeaseIdentityAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<PersistedLeaseIdentity?>(null);
    }

    /// <summary>Persisted ownership generation returned by provider-specific test queries.</summary>
    protected readonly record struct PersistedLeaseIdentity(DateTimeOffset LockedUntil, string? Owner);

    /// <summary>Controllable membership used by storage-provider conformance tests to stamp the owner identity.</summary>
    protected ControlledNodeMembership NodeMembership { get; } = new();

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
    private static long _messageIdCounter;

    /// <summary>Creates a valid message for testing.</summary>
    protected static Message CreateMessage(string? messageId = null, string? messageName = null, object? value = null)
    {
        var id = messageId ?? $"msg-{Interlocked.Increment(ref _messageIdCounter)}";

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, id },
            { MessagingHeaders.MessageName, messageName ?? "TestMessage" },
            { MessagingHeaders.SentTime, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
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

        // then
        publishedTable.Should().NotBeNullOrEmpty();
        receivedTable.Should().NotBeNullOrEmpty();

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
        result.StorageId.Should().NotBe(Guid.Empty);
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
        result.StorageId.Should().NotBe(Guid.Empty);
        result.Origin.Id.Should().Be("non-numeric-id");
    }

    public virtual async Task should_store_published_message_with_intent_type()
    {
        // given
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support monitoring roundtrip");
        }

        var storage = GetStorage();
        var message = CreateMessage();
        var envelope = new MediumMessage
        {
            StorageId = Guid.Empty,
            Origin = message,
            Content = string.Empty,
            IntentType = IntentType.Queue,
        };

        // when
        var result = await storage.StoreMessageAsync("test-published-message", envelope, cancellationToken: AbortToken);

        // then
        result.IntentType.Should().Be(IntentType.Queue);
        var roundTripped = await storage.GetMonitoringApi().GetPublishedMessageAsync(result.StorageId, AbortToken);
        roundTripped.Should().NotBeNull();
        roundTripped!.IntentType.Should().Be(IntentType.Queue);
    }

    public virtual async Task should_filter_monitoring_messages_by_intent_type()
    {
        // given
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support monitoring roundtrip");
        }

        var storage = GetStorage();
        await storage.StoreMessageAsync(
            "intent-filter",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                IntentType = IntentType.Bus,
            },
            cancellationToken: AbortToken
        );
        await storage.StoreMessageAsync(
            "intent-filter",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                IntentType = IntentType.Queue,
            },
            cancellationToken: AbortToken
        );

        // when
        var page = await storage
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Name = "intent-filter",
                    IntentType = IntentType.Queue,
                    PageSize = 20,
                },
                AbortToken
            );

        // then
        page.Items.Should().OnlyContain(message => message.IntentType == IntentType.Queue);
        page.Items.Should().ContainSingle();
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
        result.StorageId.Should().NotBe(Guid.Empty);
        result.Origin.Should().BeSameAs(message);
    }

    public virtual async Task should_store_received_bus_and_queue_rows_with_same_identity()
    {
        // given
        var storage = GetStorage();
        var messageId = $"same-identity-{Guid.NewGuid():N}";
        var bus = CreateMessage(messageId);
        var queue = CreateMessage(messageId);
        const string messageName = "test-received-message";
        const string group = "test-group";

        // when
        await storage.StoreReceivedMessageAsync(
            messageName,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = bus,
                Content = string.Empty,
                IntentType = IntentType.Bus,
            },
            AbortToken
        );
        await storage.StoreReceivedMessageAsync(
            messageName,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = queue,
                Content = string.Empty,
                IntentType = IntentType.Queue,
            },
            AbortToken
        );

        // then
        var rowCount = await CountReceivedMessagesByIdentityAsync(messageId, group, AbortToken);
        rowCount.Should().Be(2);
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

        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);

        // when — transition to Failed with a future NextRetryAt so the row stays mutable and the
        // state transition can be read back through the monitoring API.
        var result = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: nextRetryAt,
            cancellationToken: AbortToken
        );

        // then — the storage write must succeed and the persisted row must reflect the new state.
        result.Should().BeTrue("the state transition must succeed against a fresh, non-terminal row");

        if (Capabilities.SupportsMonitoringApi)
        {
            var roundTripped = await storage
                .GetMonitoringApi()
                .GetPublishedMessageAsync(storedMessage.StorageId, AbortToken);
            roundTripped.Should().NotBeNull("the row must persist after a successful state change");
            roundTripped!
                .NextRetryAt.Should()
                .NotBeNull("NextRetryAt must be persisted by ChangePublishStateAsync")
                .And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));
        }
    }

    public virtual async Task should_change_receive_state()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync("state-test", "group", message, AbortToken);

        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);

        // when — transition to Failed with a future NextRetryAt so the row stays mutable and the
        // state transition can be read back through the monitoring API.
        var result = await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: nextRetryAt,
            cancellationToken: AbortToken
        );

        // then — the storage write must succeed and the persisted row must reflect the new state.
        result.Should().BeTrue("the state transition must succeed against a fresh, non-terminal row");

        if (Capabilities.SupportsMonitoringApi)
        {
            var roundTripped = await storage
                .GetMonitoringApi()
                .GetReceivedMessageAsync(storedMessage.StorageId, AbortToken);
            roundTripped.Should().NotBeNull("the row must persist after a successful state change");
            roundTripped!
                .NextRetryAt.Should()
                .NotBeNull("NextRetryAt must be persisted by ChangeReceiveStateAsync")
                .And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));
        }
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

    public virtual async Task should_not_flip_terminal_published_row_back_to_delayed()
    {
        if (!Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling");
        }

        // given — a row sealed terminal (Succeeded, no scheduled retry). The dispatcher's shutdown
        // flush (DisposeAsync → ChangePublishStateToDelayedAsync) can race a consumer that just
        // dispatched the same row; once one side seals it, the flush must not resurrect it as Delayed.
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "terminal-delayed-guard",
            CreateMessage(),
            cancellationToken: AbortToken
        );

        var sealedFirst = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Succeeded,
            nextRetryAt: null,
            cancellationToken: AbortToken
        );
        sealedFirst.Should().BeTrue("the transition to Succeeded must win against a fresh row");

        // when — the late shutdown flush tries to move the sealed row back to Delayed.
        await storage.ChangePublishStateToDelayedAsync([storedMessage.StorageId], AbortToken);

        // then — the row must remain terminal: a follow-up state change is still rejected by the
        // terminal guard (a Delayed row would have accepted it) and the retry pickup never sees it.
        var lateChange = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Scheduled,
            nextRetryAt: DateTimeOffset.UtcNow,
            cancellationToken: AbortToken
        );
        lateChange.Should().BeFalse("the terminal seal must survive a late scheduler flush");

        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken);
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_ignore_unknown_storage_ids_when_flushing_delayed_state()
    {
        if (!Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling");
        }

        // given — one live row plus an id with no backing row (e.g. the row was pruned between the
        // dispatcher snapshotting its scheduler queue and the shutdown flush running).
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "delayed-unknown-id",
            CreateMessage(),
            cancellationToken: AbortToken
        );

        // when
        var act = async () =>
            await storage.ChangePublishStateToDelayedAsync([storedMessage.StorageId, Guid.NewGuid()], AbortToken);

        // then
        await act.Should().NotThrowAsync("missing rows must be skipped, not faulted on");
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
        var timeout = DateTimeOffset.UtcNow.AddMinutes(-10);

        // when
        var deletedCount = await storage.DeleteExpiresAsync(tableName, timeout, 100, AbortToken);

        // then
        deletedCount.Should().BeGreaterThanOrEqualTo(0);
    }

    public virtual async Task should_not_delete_expired_failed_messages_with_pending_retry()
    {
        if (!Capabilities.SupportsExpiration || !Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support expiration and monitoring roundtrip");
        }

        // given — Failed rows with a future NextRetryAt are retry-scheduled, not terminal poison.
        // Expiration cleanup must only delete Failed/Succeeded rows once NextRetryAt is cleared.
        var storage = GetStorage();
        var initializer = GetInitializer();
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var cleanupCutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(10);

        var published = await storage.StoreMessageAsync(
            "retry-expiration-published",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        published.ExpiresAt = expiredAt;

        var publishedChanged = await storage.ChangePublishStateAsync(
            published,
            StatusName.Failed,
            nextRetryAt: nextRetryAt,
            cancellationToken: AbortToken
        );
        publishedChanged.Should().BeTrue();

        var received = await storage.StoreReceivedMessageAsync(
            "retry-expiration-received",
            "retry-expiration-group",
            CreateMessage(),
            AbortToken
        );
        received.ExpiresAt = expiredAt;

        var receivedChanged = await storage.ChangeReceiveStateAsync(
            received,
            StatusName.Failed,
            nextRetryAt: nextRetryAt,
            cancellationToken: AbortToken
        );
        receivedChanged.Should().BeTrue();

        // when
        var deletedPublished = await storage.DeleteExpiresAsync(
            initializer.GetPublishedTableName(),
            cleanupCutoff,
            100,
            AbortToken
        );
        var deletedReceived = await storage.DeleteExpiresAsync(
            initializer.GetReceivedTableName(),
            cleanupCutoff,
            100,
            AbortToken
        );

        // then
        deletedPublished.Should().Be(0);
        deletedReceived.Should().Be(0);

        var persistedPublished = await storage
            .GetMonitoringApi()
            .GetPublishedMessageAsync(published.StorageId, AbortToken);
        persistedPublished.Should().NotBeNull();
        persistedPublished!.NextRetryAt.Should().NotBeNull().And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));

        var persistedReceived = await storage
            .GetMonitoringApi()
            .GetReceivedMessageAsync(received.StorageId, AbortToken);
        persistedReceived.Should().NotBeNull();
        persistedReceived!.NextRetryAt.Should().NotBeNull().And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));
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
                    $"concurrent-messageName-{i}",
                    message,
                    cancellationToken: AbortToken
                );
                results.Add(result);
            });

        await Task.WhenAll(tasks);

        // then
        results.Should().HaveCount(20);
        results.Should().AllSatisfy(r => r.StorageId.Should().NotBe(Guid.Empty));
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

    public virtual async Task should_claim_delayed_messages_atomically_when_capability_supported()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        var now = TimeProvider.GetUtcNow();
        var later = await storage.StoreMessageAsync(
            "delayed-claim-later",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                IntentType = IntentType.Bus,
                ExpiresAt = now.AddSeconds(30),
            },
            cancellationToken: AbortToken
        );
        var earlier = await storage.StoreMessageAsync(
            "delayed-claim-earlier",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                IntentType = IntentType.Bus,
                ExpiresAt = now.AddSeconds(20),
            },
            cancellationToken: AbortToken
        );
        later.ExpiresAt = now.AddSeconds(30);
        earlier.ExpiresAt = now.AddSeconds(20);
        (await storage.ChangePublishStateAsync(later, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        (await storage.ChangePublishStateAsync(earlier, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        var claimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);

        claimed.Select(message => message.StorageId).Should().Equal(earlier.StorageId, later.StorageId);
        claimed.Should().AllSatisfy(message => message.LockedUntil.Should().NotBeNull());
        (await claimStorage.ClaimDelayedMessagesAsync(AbortToken))
            .Should()
            .BeEmpty("the live claim lease must fence an immediate re-poll");
    }

    public virtual async Task should_keep_early_delayed_claim_lease_alive_until_dispatch()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        var dispatchTimeout = TimeSpan.FromSeconds(1);
        if (!TrySetDispatchTimeout(dispatchTimeout))
        {
            Assert.Skip("Storage does not expose mutable dispatch-timeout options");
            return;
        }

        var expiresAt = TimeProvider.GetUtcNow().AddSeconds(30);
        var stored = await storage.StoreMessageAsync(
            "delayed-claim-short-timeout",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                IntentType = IntentType.Bus,
                ExpiresAt = expiresAt,
            },
            cancellationToken: AbortToken
        );
        stored.ExpiresAt = expiresAt;
        (await storage.ChangePublishStateAsync(stored, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        var claimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);

        var winner = claimed.Should().ContainSingle().Subject;
        winner.StorageId.Should().Be(stored.StorageId);
        winner.LockedUntil.Should().NotBeNull();
        winner.LockedUntil!.Value.Should().BeOnOrAfter(expiresAt.Add(dispatchTimeout));
    }

    public virtual async Task should_clear_claim_lease_when_flushing_delayed_state()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        // A long dispatch timeout makes the claim lease clearly future-dated, so a stale (un-cleared) lease would
        // fence the row from re-claim on restart. The graceful-shutdown flush must release the lease so the row
        // is immediately re-claimable — otherwise the delayed message is delivered up to DispatchTimeout late.
        var dispatchTimeout = TimeSpan.FromSeconds(120);
        if (!TrySetDispatchTimeout(dispatchTimeout))
        {
            Assert.Skip("Storage does not expose mutable dispatch-timeout options");
            return;
        }

        var expiresAt = TimeProvider.GetUtcNow().AddSeconds(30);
        var stored = await storage.StoreMessageAsync(
            "delayed-claim-flush-clears-lease",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                IntentType = IntentType.Bus,
                ExpiresAt = expiresAt,
            },
            cancellationToken: AbortToken
        );
        stored.ExpiresAt = expiresAt;
        (await storage.ChangePublishStateAsync(stored, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        // Claim it — stamps a future-dated ownership lease and moves it to Queued.
        var claimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);
        claimed.Should().ContainSingle().Which.StorageId.Should().Be(stored.StorageId);

        // Flush it back to Delayed, exactly as the graceful-shutdown scheduler flush does.
        await storage.ChangePublishStateToDelayedAsync([stored.StorageId], AbortToken);

        // The flush must clear the lease so the row is immediately re-claimable. With a stale lease this re-poll
        // returns empty (the message would wait out DispatchTimeout before re-dispatch).
        var reclaimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);
        reclaimed
            .Should()
            .ContainSingle("the graceful-shutdown flush must release the claim lease for immediate re-scheduling")
            .Which.StorageId.Should()
            .Be(stored.StorageId);
    }

    public virtual async Task should_return_disjoint_winners_to_concurrent_delayed_claimers()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        const int messageCount = 8;
        var now = TimeProvider.GetUtcNow();
        var storageIds = new HashSet<Guid>();
        for (var index = 0; index < messageCount; index++)
        {
            var expiresAt = now.AddSeconds(10 + index);
            var stored = await storage.StoreMessageAsync(
                $"concurrent-delayed-claim-{index}",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = CreateMessage(),
                    Content = string.Empty,
                    IntentType = IntentType.Bus,
                    ExpiresAt = expiresAt,
                },
                cancellationToken: AbortToken
            );
            stored.ExpiresAt = expiresAt;
            (await storage.ChangePublishStateAsync(stored, StatusName.Delayed, cancellationToken: AbortToken))
                .Should()
                .BeTrue();
            storageIds.Add(stored.StorageId);
        }

        var claims = await Task.WhenAll(
            claimStorage.ClaimDelayedMessagesAsync(AbortToken).AsTask(),
            claimStorage.ClaimDelayedMessagesAsync(AbortToken).AsTask()
        );
        var claimedIds = claims.SelectMany(messages => messages).Select(message => message.StorageId).ToArray();

        claimedIds.Should().HaveCount(messageCount).And.OnlyHaveUniqueItems();
        claimedIds.Should().BeEquivalentTo(storageIds);
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
        result.StorageId.Should().NotBe(Guid.Empty);
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
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
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

    public virtual async Task should_seal_succeeded_published_message_against_state_change_and_retry_pickup()
    {
        // given — Succeeded with NextRetryAt = NULL is the terminal fingerprint written after a
        // successful dispatch. This is the double-dispatch closure: the commit-edge drain and the
        // relay sweep can both attempt the same row in a narrow window, so once one of them seals
        // it, the row must reject any late state change AND never be returned by the retry pickup.
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync(
            "succeeded-terminal-seal",
            message,
            cancellationToken: AbortToken
        );

        var sealedFirst = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Succeeded,
            nextRetryAt: null,
            cancellationToken: AbortToken
        );
        sealedFirst.Should().BeTrue("the first transition to Succeeded must win against a fresh row");

        // when — a late writer (the losing side of the drain/relay race) tries to flip the row back.
        var lateChange = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Scheduled,
            nextRetryAt: DateTimeOffset.UtcNow,
            cancellationToken: AbortToken
        );

        // then — the terminal guard rejects the write and the pickup never re-sends the row.
        lateChange.Should().BeFalse("a Succeeded row with no scheduled retry is terminal");

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
            nextRetryAt: DateTimeOffset.UtcNow.AddHours(1),
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
            nextRetryAt: DateTimeOffset.UtcNow.AddHours(1),
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);
        retriable.Should().NotBeNull();
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_leased_published_message_until_lease_expires()
    {
        // Verifies the lease/pickup contract:
        //   1. An active lease (LockedUntil in the future) excludes the row from retry pickup.
        //   2. After the lease window elapses (LockedUntil <= now), the row is eligible again.
        //
        // The lease-contention guard from PR #254 review #15 rejects an attempt to overwrite an
        // active lease with a past timestamp — so the old "negative-timestamp trick" no longer
        // works. The test instead writes a short real-clock lease and waits for it to expire,
        // matching how production code would observe lease expiry.
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "leased-published",
            CreateMessage(),
            cancellationToken: AbortToken
        );

        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var leaseWindow = TimeSpan.FromMilliseconds(500);
        var leased = await storage.LeasePublishAsync(storedMessage, leaseWindow, AbortToken);

        leased.Should().BeTrue();
        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        await Task.Delay(leaseWindow + TimeSpan.FromMilliseconds(250), AbortToken);

        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_leased_received_message_until_lease_expires()
    {
        // Asymmetric coverage parity with the published-lease test above. See that method for
        // the rationale behind the short real-clock lease window.
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
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var leaseWindow = TimeSpan.FromMilliseconds(500);
        var leased = await storage.LeaseReceiveAsync(storedMessage, leaseWindow, AbortToken);

        leased.Should().BeTrue();
        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        await Task.Delay(leaseWindow + TimeSpan.FromMilliseconds(250), AbortToken);

        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_use_database_clock_when_reclaiming_published_retry_lease()
    {
        var fastClockStorage = _CreateRelationalClockSkewStorage();

        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "db-clock-published-retry",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );
        (await storage.LeasePublishAsync(storedMessage, TimeSpan.FromMinutes(30), AbortToken)).Should().BeTrue();

        (await fastClockStorage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_stamp_fresh_dispatch_lease_from_database_clock(bool published, bool reserveAttempt)
    {
        var skewedStorage = _CreateRelationalClockSkewStorage();
        var databaseTimeBefore = await GetDatabaseUtcNowAsync(AbortToken);
        if (databaseTimeBefore is null)
        {
            Assert.Skip("Storage does not expose a relational database-clock test seam");
        }

        var storage = GetStorage();
        var message = published
            ? await storage.StoreMessageAsync("db-clock-dispatch-lease", CreateMessage(), cancellationToken: AbortToken)
            : await storage.StoreReceivedMessageAsync(
                "db-clock-dispatch-lease",
                "db-clock-dispatch-group",
                CreateMessage(),
                AbortToken
            );
        var originalOwner = NodeMembership.SetIdentity($"database-clock-{published}-{reserveAttempt}");
        var leaseDuration = TimeSpan.FromSeconds(5);
        message.InlineAttempts = reserveAttempt ? 1 : 0;

        var acquired = (published, reserveAttempt) switch
        {
            (true, false) => await skewedStorage.LeasePublishAsync(message, leaseDuration, AbortToken),
            (true, true) => await skewedStorage.LeasePublishAndReserveAttemptAsync(
                message,
                leaseDuration,
                originalInlineAttempts: 0,
                AbortToken
            ),
            (false, false) => await skewedStorage.LeaseReceiveAsync(message, leaseDuration, AbortToken),
            (false, true) => await skewedStorage.LeaseReceiveAndReserveAttemptAsync(
                message,
                leaseDuration,
                originalInlineAttempts: 0,
                AbortToken
            ),
        };

        var databaseTimeAfter = await GetDatabaseUtcNowAsync(AbortToken);
        var persisted = await GetPersistedLeaseIdentityAsync(published, message.StorageId, AbortToken);
        acquired.Should().BeTrue();
        databaseTimeAfter.Should().NotBeNull();
        persisted.Should().NotBeNull();
        var persistedLockedUntil = persisted!.Value.LockedUntil;
        var databaseTimeBeforeUtc = new DateTimeOffset(
            DateTime.SpecifyKind(databaseTimeBefore.Value, DateTimeKind.Utc),
            TimeSpan.Zero
        );
        var databaseTimeAfterUtc = new DateTimeOffset(
            DateTime.SpecifyKind(databaseTimeAfter!.Value, DateTimeKind.Utc),
            TimeSpan.Zero
        );
        message.LockedUntil.Should().Be(persistedLockedUntil);
        message.Owner.Should().Be(persisted.Value.Owner).And.Be(originalOwner.ToString());
        message
            .LockedUntil.Should()
            .BeOnOrAfter(databaseTimeBeforeUtc.Add(leaseDuration))
            .And.BeOnOrBefore(databaseTimeAfterUtc.Add(leaseDuration));

        NodeMembership.SetIdentity("database-clock-contender");
        var reacquired = published
            ? await skewedStorage.LeasePublishAsync(message, leaseDuration, AbortToken)
            : await skewedStorage.LeaseReceiveAsync(message, leaseDuration, AbortToken);
        reacquired.Should().BeFalse("the database-authored lease is still active");
    }

    public virtual async Task should_use_database_clock_when_reclaiming_received_retry_lease()
    {
        var fastClockStorage = _CreateRelationalClockSkewStorage();

        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "db-clock-received-retry",
            "db-clock-group",
            CreateMessage(),
            AbortToken
        );
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );
        (await storage.LeaseReceiveAsync(storedMessage, TimeSpan.FromMinutes(30), AbortToken)).Should().BeTrue();

        (await fastClockStorage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_use_database_clock_when_fast_forwarding_dead_owner_lease()
    {
        var fastClockStorage = _CreateRelationalClockSkewStorage();

        var deadOwner = NodeMembership.SetIdentity("db-clock-dead-owner");
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "db-clock-dead-owner-reclaim",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );
        (await storage.LeasePublishAsync(storedMessage, TimeSpan.FromMinutes(30), AbortToken)).Should().BeTrue();

        (await fastClockStorage.ReclaimDeadPublishedOwnersAsync([deadOwner.ToString()], AbortToken)).Should().Be(1);
        (await fastClockStorage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_stamp_retry_lease_from_database_clock()
    {
        var fastClockStorage = _CreateRelationalClockSkewStorage();
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "db-clock-retry-stamp",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );

        var claimed = (await fastClockStorage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == storedMessage.StorageId)
            .Subject;

        claimed.LockedUntil.Should().BeAfter(DateTimeOffset.UtcNow).And.BeBefore(DateTimeOffset.UtcNow.AddMinutes(10));
    }

    public virtual async Task should_use_application_clock_when_scheduling_published_retry()
    {
        var (storage, schedulingClock) = _CreateRelationalSchedulingClockStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "application-clock-published-retry",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: schedulingClock.GetUtcNow().AddMinutes(1),
            cancellationToken: AbortToken
        );

        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        schedulingClock.Advance(TimeSpan.FromMinutes(2));

        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_use_application_clock_when_scheduling_received_retry()
    {
        var (storage, schedulingClock) = _CreateRelationalSchedulingClockStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "application-clock-received-retry",
            "application-clock-group",
            CreateMessage(),
            AbortToken
        );
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: schedulingClock.GetUtcNow().AddMinutes(1),
            cancellationToken: AbortToken
        );

        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        schedulingClock.Advance(TimeSpan.FromMinutes(2));

        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_return_unstored_snapshot_when_redelivery_hits_active_receive_lease()
    {
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "active-lease-redelivery",
            "test-group",
            message,
            AbortToken
        );
        var now = _Now();

        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: now.AddSeconds(-1),
            cancellationToken: AbortToken
        );
        var leased = await storage.LeaseReceiveAsync(storedMessage, TimeSpan.FromMinutes(5), AbortToken);
        var beforeRedelivery = Capabilities.SupportsMonitoringApi
            ? await storage.GetMonitoringApi().GetReceivedMessageAsync(storedMessage.StorageId, AbortToken)
            : null;

        var redelivery = await storage.StoreReceivedMessageAsync(
            "active-lease-redelivery",
            "test-group",
            message,
            AbortToken
        );

        leased.Should().BeTrue();
        redelivery.StorageId.Should().NotBe(storedMessage.StorageId);
        redelivery.LockedUntil.Should().BeNull();
        redelivery.Owner.Should().BeNull();
        (await storage.LeaseReceiveAsync(redelivery, TimeSpan.FromMinutes(5), AbortToken))
            .Should()
            .BeFalse("the guard-blocked upsert returned an unpersisted candidate");

        if (beforeRedelivery is not null)
        {
            var afterRedelivery = await storage
                .GetMonitoringApi()
                .GetReceivedMessageAsync(storedMessage.StorageId, AbortToken);
            afterRedelivery.Should().NotBeNull();
            afterRedelivery!.Content.Should().Be(beforeRedelivery.Content);
            afterRedelivery.LockedUntil.Should().Be(beforeRedelivery.LockedUntil);
            afterRedelivery.Owner.Should().Be(beforeRedelivery.Owner);
            afterRedelivery.Retries.Should().Be(beforeRedelivery.Retries);
            afterRedelivery.ExceptionInfo.Should().Be(beforeRedelivery.ExceptionInfo);
        }
    }

    public virtual async Task should_reclaim_published_retry_row_owned_by_dead_node()
    {
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("dead-published-owner");
        var deadOwned = await _StoreFailedPublishedMessageAsync("dead-owned-published");
        var deadLease = await storage.LeasePublishAsync(deadOwned, TimeSpan.FromHours(1), AbortToken);
        deadLease.Should().BeTrue("the dead-owned row must be actively leased before reclaim runs");

        var liveOwner = NodeMembership.SetIdentity("live-published-owner");
        var liveOwned = await _StoreFailedPublishedMessageAsync("live-owned-published");
        var liveLease = await storage.LeasePublishAsync(liveOwned, TimeSpan.FromHours(1), AbortToken);
        liveLease.Should().BeTrue("the live-owned row must be actively leased before reclaim runs");

        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == deadOwned.StorageId || m.StorageId == liveOwned.StorageId);

        var reclaimed = await storage.ReclaimDeadPublishedOwnersAsync([deadOwner.ToString()], AbortToken);

        reclaimed.Should().Be(1);
        var retriable = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();
        retriable.Should().Contain(m => m.StorageId == deadOwned.StorageId);
        retriable.Should().NotContain(m => m.StorageId == liveOwned.StorageId);
        deadOwner.ToString().Should().NotBe(liveOwner.ToString());
    }

    public virtual async Task should_reclaim_received_retry_row_owned_by_dead_node()
    {
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("dead-received-owner");
        var deadOwned = await _StoreFailedReceivedMessageAsync("dead-owned-received", "dead-group");
        var deadLease = await storage.LeaseReceiveAsync(deadOwned, TimeSpan.FromHours(1), AbortToken);
        deadLease.Should().BeTrue("the dead-owned row must be actively leased before reclaim runs");

        var liveOwner = NodeMembership.SetIdentity("live-received-owner");
        var liveOwned = await _StoreFailedReceivedMessageAsync("live-owned-received", "live-group");
        var liveLease = await storage.LeaseReceiveAsync(liveOwned, TimeSpan.FromHours(1), AbortToken);
        liveLease.Should().BeTrue("the live-owned row must be actively leased before reclaim runs");

        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == deadOwned.StorageId || m.StorageId == liveOwned.StorageId);

        var reclaimed = await storage.ReclaimDeadReceivedOwnersAsync([deadOwner.ToString()], AbortToken);

        reclaimed.Should().Be(1);
        var retriable = (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken)).ToList();
        retriable.Should().Contain(m => m.StorageId == deadOwned.StorageId);
        retriable.Should().NotContain(m => m.StorageId == liveOwned.StorageId);
        deadOwner.ToString().Should().NotBe(liveOwner.ToString());
    }

    public virtual async Task should_stamp_owner_on_claim()
    {
        var storage = GetStorage();
        var owner = NodeMembership.SetIdentity("claim-owner", incarnation: 7);
        var expectedOwner = owner.ToString();

        var published = await _StoreFailedPublishedMessageAsync("claim-owner-published");
        var claimedPublished = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == published.StorageId)
            .Subject;

        claimedPublished.Owner.Should().Be(expectedOwner);
        claimedPublished.LockedUntil.Should().NotBeNull();

        var received = await _StoreFailedReceivedMessageAsync("claim-owner-received", "claim-group");
        var claimedReceived = (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == received.StorageId)
            .Subject;

        claimedReceived.Owner.Should().Be(expectedOwner);
        claimedReceived.LockedUntil.Should().NotBeNull();
    }

    public virtual async Task should_not_reclaim_rows_of_live_or_restarted_incarnation()
    {
        var storage = GetStorage();
        // The crashed incarnation (@7) is the dead owner; the restarted incarnation (@8) is live.
        // Reclaiming the dead set must fence the restart: @8's rows stay untouched.
        var deadOwner = NodeMembership.SetIdentity("restart-node", incarnation: 7);
        var oldPublished = await _StoreFailedPublishedMessageAsync("old-incarnation-published");
        (await storage.LeasePublishAsync(oldPublished, _FutureLease(), AbortToken)).Should().BeTrue();
        var oldReceived = await _StoreFailedReceivedMessageAsync("old-incarnation-received", "old-incarnation-group");
        (await storage.LeaseReceiveAsync(oldReceived, _FutureLease(), AbortToken)).Should().BeTrue();

        var liveOwner = NodeMembership.SetIdentity("restart-node", incarnation: 8);
        var livePublished = await _StoreFailedPublishedMessageAsync("live-incarnation-published");
        (await storage.LeasePublishAsync(livePublished, _FutureLease(), AbortToken)).Should().BeTrue();
        var liveReceived = await _StoreFailedReceivedMessageAsync(
            "live-incarnation-received",
            "live-incarnation-group"
        );
        (await storage.LeaseReceiveAsync(liveReceived, _FutureLease(), AbortToken)).Should().BeTrue();

        deadOwner.ToString().Should().NotBe(liveOwner.ToString());
        var deadOwners = new[] { deadOwner.ToString() };

        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken)).Should().Be(1);
        var publishedRetriable = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();
        publishedRetriable.Should().Contain(m => m.StorageId == oldPublished.StorageId);
        publishedRetriable.Should().NotContain(m => m.StorageId == livePublished.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(1);
        var receivedRetriable = (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken)).ToList();
        receivedRetriable.Should().Contain(m => m.StorageId == oldReceived.StorageId);
        receivedRetriable.Should().NotContain(m => m.StorageId == liveReceived.StorageId);
    }

    public virtual async Task should_not_reclaim_terminal_rows()
    {
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("terminal-dead-owner");
        var published = await _StoreFailedPublishedMessageAsync("terminal-published");
        (await storage.LeasePublishAsync(published, _FutureLease(), AbortToken)).Should().BeTrue();
        (
            await storage.ChangePublishStateAsync(
                published,
                StatusName.Failed,
                nextRetryAt: null,
                lockedUntil: _FutureLeaseUntil(),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var received = await _StoreFailedReceivedMessageAsync("terminal-received", "terminal-group");
        (await storage.LeaseReceiveAsync(received, _FutureLease(), AbortToken)).Should().BeTrue();
        (
            await storage.ChangeReceiveStateAsync(
                received,
                StatusName.Failed,
                nextRetryAt: null,
                lockedUntil: _FutureLeaseUntil(),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var deadOwners = new[] { deadOwner.ToString() };

        // A terminal row owned by a dead owner is matched by the owner clause but excluded by the
        // terminal-row guard, so reclaim leaves it alone.
        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken))
            .Should()
            .Be(0);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == published.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_be_inert_when_no_dead_owners_passed()
    {
        var storage = GetStorage();
        var published = await _StoreFailedPublishedMessageAsync("owner-null-published");
        (await storage.LeasePublishAsync(published, _FutureLease(), AbortToken)).Should().BeTrue();
        var received = await _StoreFailedReceivedMessageAsync("owner-null-received", "owner-null-group");
        (await storage.LeaseReceiveAsync(received, _FutureLease(), AbortToken)).Should().BeTrue();

        (await storage.ReclaimDeadPublishedOwnersAsync([], AbortToken)).Should().Be(0);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == published.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync([], AbortToken)).Should().Be(0);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_not_reclaim_rows_with_null_owner()
    {
        var storage = GetStorage();
        // NodeMembership.Identity is null by default — rows get Owner=NULL when leased
        var published = await _StoreFailedPublishedMessageAsync("null-owner-guard-published");
        (await storage.LeasePublishAsync(published, _FutureLease(), AbortToken)).Should().BeTrue();
        var received = await _StoreFailedReceivedMessageAsync("null-owner-guard-received", "null-owner-guard-group");
        (await storage.LeaseReceiveAsync(received, _FutureLease(), AbortToken)).Should().BeTrue();

        // Non-empty list bypasses early-exit guard; WHERE Owner IS NOT NULL must filter null-Owner rows
        (await storage.ReclaimDeadPublishedOwnersAsync(["dead-owner-x"], AbortToken))
            .Should()
            .Be(0);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == published.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync(["dead-owner-x"], AbortToken)).Should().Be(0);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_reclaim_dead_owner_rows_idempotently()
    {
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("idempotent-dead-owner");
        var published = await _StoreFailedPublishedMessageAsync("idempotent-published");
        (await storage.LeasePublishAsync(published, _FutureLease(), AbortToken)).Should().BeTrue();
        var received = await _StoreFailedReceivedMessageAsync("idempotent-received", "idempotent-group");
        (await storage.LeaseReceiveAsync(received, _FutureLease(), AbortToken)).Should().BeTrue();

        var deadOwners = new[] { deadOwner.ToString() };

        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken)).Should().Be(1);
        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == published.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(1);
        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_not_reclaim_dead_owner_rows_with_expired_lease()
    {
        // AE4: the LockedUntil floor — a dead owner's row whose lease has already expired is left
        // untouched by reclaim (the `LockedUntil > now` clause excludes it); normal lease-expiry
        // pickup recovers it. Reclaim only fast-forwards leases still in the future.
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("expired-lease-dead-owner");
        var published = await _StoreFailedPublishedMessageAsync("expired-lease-published");
        (await storage.LeasePublishAsync(published, TimeSpan.FromSeconds(-1), AbortToken)).Should().BeTrue();
        var received = await _StoreFailedReceivedMessageAsync("expired-lease-received", "expired-lease-group");
        (await storage.LeaseReceiveAsync(received, TimeSpan.FromSeconds(-1), AbortToken)).Should().BeTrue();

        var deadOwners = new[] { deadOwner.ToString() };

        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);
        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);

        // The floor still recovers them: an expired lease is already retriable via normal pickup.
        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == published.StorageId);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == received.StorageId);
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
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(5),
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
                            IntentType = IntentType.Bus,
                            Retries = 1,
                        };
                        var ok = await storage.ChangeReceiveStateAsync(
                            localCopy,
                            StatusName.Failed,
                            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(10),
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
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            originalRetries: 0,
            cancellationToken: AbortToken
        );

        var second = await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            originalRetries: 0,
            cancellationToken: AbortToken
        );

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    public virtual async Task should_lease_and_reserve_publish_attempt_in_single_step()
    {
        var storage = GetStorage();
        var message = await storage.StoreMessageAsync("lease-reserve", CreateMessage(), cancellationToken: AbortToken);
        message.InlineAttempts = 1;

        var reserved = await storage.LeasePublishAndReserveAttemptAsync(
            message,
            TimeSpan.FromMinutes(5),
            originalInlineAttempts: 0,
            AbortToken
        );

        reserved.Should().BeTrue();
        message.LockedUntil.Should().NotBeNull("a successful combined write must mirror the lease to the caller");

        // The row is now actively leased: a second combined write must be rejected even with the
        // correct counter token, while the standalone mid-burst reservation under the held lease
        // still succeeds.
        message.InlineAttempts = 2;
        var contended = await storage.LeasePublishAndReserveAttemptAsync(
            message,
            TimeSpan.FromMinutes(5),
            originalInlineAttempts: 1,
            AbortToken
        );
        contended.Should().BeFalse("an actively leased row must not be re-leased mid-burst");

        var midBurst = await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 1, AbortToken);
        midBurst.Should().BeTrue("the lease owner must still be able to reserve the next inline attempt");
    }

    public virtual async Task should_reject_lease_and_reserve_with_stale_inline_attempts_token()
    {
        var storage = GetStorage();
        var message = await storage.StoreReceivedMessageAsync(
            "lease-reserve-cas",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        // First combined write with an already-expired lease leaves the row unleased but advances
        // the durable InlineAttempts counter to 1.
        message.InlineAttempts = 1;
        var first = await storage.LeaseReceiveAndReserveAttemptAsync(
            message,
            TimeSpan.FromSeconds(-1),
            originalInlineAttempts: 0,
            AbortToken
        );
        first.Should().BeTrue();

        // A contender holding a stale counter view (0) must fail the CAS; the current token (1)
        // must succeed.
        message.InlineAttempts = 2;
        var stale = await storage.LeaseReceiveAndReserveAttemptAsync(
            message,
            TimeSpan.FromMinutes(5),
            originalInlineAttempts: 0,
            AbortToken
        );
        stale.Should().BeFalse("the durable InlineAttempts token moved; a stale view must not re-reserve");

        var current = await storage.LeaseReceiveAndReserveAttemptAsync(
            message,
            TimeSpan.FromMinutes(5),
            originalInlineAttempts: 1,
            AbortToken
        );
        current.Should().BeTrue();
    }

    public virtual Task should_reject_stale_published_lease_generation_writes()
    {
        return _ShouldRejectStaleLeaseGenerationWritesAsync(received: false);
    }

    public virtual Task should_reject_stale_received_lease_generation_writes()
    {
        return _ShouldRejectStaleLeaseGenerationWritesAsync(received: true);
    }

    public virtual Task should_allow_published_fenced_writes_with_fast_application_clock()
    {
        return _ShouldAllowFencedWritesWithFastApplicationClockAsync(received: false);
    }

    public virtual Task should_allow_received_fenced_writes_with_fast_application_clock()
    {
        return _ShouldAllowFencedWritesWithFastApplicationClockAsync(received: true);
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

        await runFirstInsertStormAsync(group: null);
        await runFirstInsertStormAsync(group: "g1");

        return;

        async Task runFirstInsertStormAsync(string? group)
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

        await runStoreReceivedStormAsync(group: null);
        await runStoreReceivedStormAsync(group: "consume-group");

        return;

        async Task runStoreReceivedStormAsync(string? group)
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

    public virtual async Task should_respect_initial_dispatch_grace()
    {
        // #10 — parity test for the InitialDispatchGrace exclusion contract. Providers that
        // expose a controllable TimeProvider exercise the WHERE-predicate boundary; providers
        // backed by TimeProvider.System (or by a DB-side time function) skip until they grow a
        // fixture-level clock injection seam.
        if (!SupportsControllableClock)
        {
            Assert.Skip(
                "Provider does not expose a controllable TimeProvider — initial-dispatch-grace boundary requires FakeTimeProvider."
            );
        }

        var fakeClock = TimeProvider as Microsoft.Extensions.Time.Testing.FakeTimeProvider;
        if (fakeClock is null)
        {
            Assert.Skip("TimeProvider override is not a FakeTimeProvider — cannot advance the clock for this test.");
        }

        // given — fresh published row carries NextRetryAt = Added + InitialDispatchGrace
        // (default 30s). Polling immediately must exclude it.
        var storage = GetStorage();
        var stored = await storage.StoreMessageAsync("grace-base", CreateMessage(), cancellationToken: AbortToken);

        var beforeGrace = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();
        beforeGrace
            .Should()
            .NotContain(
                m => m.StorageId == stored.StorageId,
                "freshly-stored rows must be excluded during the initial dispatch grace window"
            );

        // when — advance past the grace window.
        fakeClock!.Advance(TimeSpan.FromMinutes(2));

        // then — the row is now eligible for pickup.
        var afterGrace = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();
        afterGrace
            .Should()
            .Contain(
                m => m.StorageId == stored.StorageId,
                "after the grace window elapses the persisted retry processor must pick the row up"
            );
    }

    public virtual async Task should_pickup_message_at_max_persisted_retries_and_exclude_above()
    {
        // given — with MaxPersistedRetries = 4, the pickup predicate is `Retries <= 4`.
        // Retries == 4 is the LAST allowed pickup (where the helper returns Exhausted on
        // budget consumption). Retries == 5 represents the terminal state past the budget
        // and must NOT be picked up. Total dispatches = (MaxPersistedRetries + 1) = 5.
        var storage = GetStorage();

        // Boundary case 1 (published): Retries == MaxPersistedRetries → picked up.
        var atLimit = await storage.StoreMessageAsync(
            "max-retries-test-pub",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        atLimit.Retries = 4;
        await storage.ChangePublishStateAsync(
            atLimit,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // Boundary case 2 (published): Retries == MaxPersistedRetries + 1 → NOT picked up.
        var aboveLimit = await storage.StoreMessageAsync(
            "above-retries-test-pub",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        aboveLimit.Retries = 5;
        await storage.ChangePublishStateAsync(
            aboveLimit,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // when
        var retriable = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();

        // then
        retriable.Should().Contain(m => m.StorageId == atLimit.StorageId);
        retriable.Should().NotContain(m => m.StorageId == aboveLimit.StorageId);

        // Same boundary semantics for received messages. Each scenario uses a distinct message
        // (and therefore a distinct MessageId) so the (MessageId, Group) upsert identity on the
        // received table does not collapse the two cases into a single row.
        var atLimitRecv = await storage.StoreReceivedMessageAsync(
            "max-retries-test-recv",
            "group",
            CreateMessage(),
            AbortToken
        );
        atLimitRecv.Retries = 4;
        await storage.ChangeReceiveStateAsync(
            atLimitRecv,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var aboveLimitRecv = await storage.StoreReceivedMessageAsync(
            "above-retries-test-recv",
            "group",
            CreateMessage(),
            AbortToken
        );
        aboveLimitRecv.Retries = 5;
        await storage.ChangeReceiveStateAsync(
            aboveLimitRecv,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var retriableReceived = (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken)).ToList();
        retriableReceived.Should().Contain(m => m.StorageId == atLimitRecv.StorageId);
        retriableReceived.Should().NotContain(m => m.StorageId == aboveLimitRecv.StorageId);
    }

    private async Task<MediumMessage> _StoreFailedPublishedMessageAsync(string name)
    {
        var storage = GetStorage();
        var stored = await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);

        await storage.ChangePublishStateAsync(
            stored,
            StatusName.Failed,
            nextRetryAt: _Now().AddSeconds(-1),
            cancellationToken: AbortToken
        );

        return stored;
    }

    private async Task<MediumMessage> _StoreFailedReceivedMessageAsync(string name, string group)
    {
        var storage = GetStorage();
        var stored = await storage.StoreReceivedMessageAsync(name, group, CreateMessage(), AbortToken);

        await storage.ChangeReceiveStateAsync(
            stored,
            StatusName.Failed,
            nextRetryAt: _Now().AddSeconds(-1),
            cancellationToken: AbortToken
        );

        return stored;
    }

    private async Task _ShouldRejectStaleLeaseGenerationWritesAsync(bool received)
    {
        var storage = GetStorage();
        var stale = received
            ? await _StoreFailedReceivedMessageAsync("stale-generation-received", "stale-generation-group")
            : await _StoreFailedPublishedMessageAsync("stale-generation-published");

        NodeMembership.SetIdentity("stale-generation-owner-a");
        var leaseA = received
            ? await storage.LeaseReceiveAsync(stale, TimeSpan.FromSeconds(-1), AbortToken)
            : await storage.LeasePublishAsync(stale, TimeSpan.FromSeconds(-1), AbortToken);
        leaseA.Should().BeTrue();

        var successor = _CopyMessage(stale);
        NodeMembership.SetIdentity("stale-generation-owner-b");
        var leaseB = received
            ? await storage.LeaseReceiveAsync(successor, TimeSpan.FromMinutes(5), AbortToken)
            : await storage.LeasePublishAsync(successor, TimeSpan.FromMinutes(5), AbortToken);
        leaseB.Should().BeTrue();
        successor.Owner.Should().NotBe(stale.Owner);

        stale.InlineAttempts = 1;
        var reserved = received
            ? await storage.ReserveReceiveAttemptAsync(stale, originalInlineAttempts: 0, AbortToken)
            : await storage.ReservePublishAttemptAsync(stale, originalInlineAttempts: 0, AbortToken);
        reserved.Should().BeFalse("a prior lease generation must not reserve under its successor's lease");

        foreach (
            var (state, nextRetryAt) in new[]
            {
                (StatusName.Succeeded, (DateTimeOffset?)null),
                (StatusName.Failed, (DateTimeOffset?)null),
                (StatusName.Failed, _Now().AddMinutes(1)),
            }
        )
        {
            var changed = received
                ? await storage.ChangeReceiveRetryStateAsync(
                    stale,
                    state,
                    nextRetryAt,
                    lockedUntil: null,
                    originalRetries: 0,
                    originalInlineAttempts: 0,
                    AbortToken
                )
                : await storage.ChangePublishRetryStateAsync(
                    stale,
                    state,
                    nextRetryAt,
                    lockedUntil: null,
                    originalRetries: 0,
                    originalInlineAttempts: 0,
                    AbortToken
                );

            changed.Should().BeFalse("a prior lease generation must not write after successor acquisition");
        }
    }

    private async Task _ShouldAllowFencedWritesWithFastApplicationClockAsync(bool received)
    {
        var storage = _CreateRelationalClockSkewStorage();
        var message = received
            ? await storage.StoreReceivedMessageAsync(
                "fast-clock-fenced-received",
                "fast-clock-fenced-group",
                CreateMessage(),
                AbortToken
            )
            : await storage.StoreMessageAsync(
                "fast-clock-fenced-published",
                CreateMessage(),
                cancellationToken: AbortToken
            );

        NodeMembership.SetIdentity("fast-clock-fenced-owner");
        var leased = received
            ? await storage.LeaseReceiveAsync(message, TimeSpan.FromMinutes(5), AbortToken)
            : await storage.LeasePublishAsync(message, TimeSpan.FromMinutes(5), AbortToken);
        leased.Should().BeTrue();

        message.InlineAttempts = 1;
        var reserved = received
            ? await storage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken)
            : await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 0, AbortToken);
        reserved.Should().BeTrue("the active database-clock lease must accept its owner's reservation");

        message.Retries = 1;
        var changed = received
            ? await storage.ChangeReceiveRetryStateAsync(
                message,
                StatusName.Failed,
                nextRetryAt: _Now().AddMinutes(1),
                lockedUntil: null,
                originalRetries: 0,
                originalInlineAttempts: 1,
                AbortToken
            )
            : await storage.ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                nextRetryAt: _Now().AddMinutes(1),
                lockedUntil: null,
                originalRetries: 0,
                originalInlineAttempts: 1,
                AbortToken
            );
        changed.Should().BeTrue("application clock skew must not reject the active owner's fenced state write");
    }

    private static MediumMessage _CopyMessage(MediumMessage message)
    {
        return new()
        {
            StorageId = message.StorageId,
            Origin = message.Origin,
            Content = message.Content,
            IntentType = message.IntentType,
            Added = message.Added,
            ExpiresAt = message.ExpiresAt,
            NextRetryAt = message.NextRetryAt,
            LockedUntil = message.LockedUntil,
            Owner = message.Owner,
            Retries = message.Retries,
            InlineAttempts = message.InlineAttempts,
            ExceptionInfo = message.ExceptionInfo,
        };
    }

    private DateTimeOffset _Now()
    {
        return TimeProvider.GetUtcNow();
    }

    /// <summary>Lease duration long enough that the lease stays live for the whole test.</summary>
    private static TimeSpan _FutureLease()
    {
        return TimeSpan.FromHours(1);
    }

    private DateTimeOffset _FutureLeaseUntil()
    {
        return _Now().Add(_FutureLease());
    }

    private IDataStorage _CreateRelationalClockSkewStorage()
    {
        var storage = CreateStorageWithTimeProvider(
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1))
        );
        if (storage is null)
        {
            Assert.Skip("Storage does not expose a relational clock-skew test seam");
        }

        return storage;
    }

    private (
        IDataStorage Storage,
        Microsoft.Extensions.Time.Testing.FakeTimeProvider Clock
    ) _CreateRelationalSchedulingClockStorage()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        var storage = CreateStorageWithTimeProvider(clock);
        if (storage is null)
        {
            Assert.Skip("Storage does not expose a relational scheduling-clock test seam");
        }

        return (storage, clock);
    }
}
