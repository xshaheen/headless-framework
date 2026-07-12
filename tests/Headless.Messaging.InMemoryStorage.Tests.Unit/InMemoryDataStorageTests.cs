// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tests.Capabilities;

namespace Tests;

/// <summary>
/// Unit tests for the in-memory <see cref="IDataStorage"/> implementation. Inherits from
/// <see cref="DataStorageTestsBase"/> so the cross-provider parity matrix (single-branch
/// retry pickup, lease/Retries CAS, terminal-row guards, concurrent state updates) runs
/// against InMemory the same way it does for PostgreSQL and SQL Server.
/// </summary>
/// <remarks>
/// InternalsVisibleTo for this project is declared in
/// <c>Headless.Messaging.InMemoryStorage.csproj</c>, so the test class can reference the
/// <c>internal sealed</c> storage and initializer types directly without going through the
/// DI Setup. Each test gets a fresh storage instance — InMemory state is per-instance, so
/// no cross-test cleanup is needed beyond letting the storage go out of scope.
/// </remarks>
public sealed class InMemoryDataStorageTests : DataStorageTestsBase
{
    private InMemoryStorageInitializer? _initializer;
    private InMemoryDataStorage? _storage;
    private ISerializer? _serializer;
    private FakeTimeProvider? _fakeTimeProvider;

    /// <inheritdoc />
    protected override TimeProvider TimeProvider
    {
        get
        {
            _EnsureInitialized();
            return _fakeTimeProvider!;
        }
    }

    /// <inheritdoc />
    protected override bool SupportsControllableClock => true;

    /// <inheritdoc />
    protected override DataStorageCapabilities Capabilities =>
        new()
        {
            SupportsExpiration = true,
            SupportsConcurrentOperations = true,
            SupportsDelayedScheduling = true,
            SupportsMonitoringApi = true,
        };

    /// <inheritdoc />
    protected override IDataStorage GetStorage()
    {
        _EnsureInitialized();
        return _storage!;
    }

    /// <inheritdoc />
    protected override IStorageInitializer GetInitializer()
    {
        _EnsureInitialized();
        return _initializer!;
    }

    /// <inheritdoc />
    protected override ISerializer GetSerializer()
    {
        _EnsureInitialized();
        return _serializer!;
    }

    /// <inheritdoc />
    protected override Task<int> CountReceivedMessagesByIdentityAsync(
        string messageId,
        string? group,
        CancellationToken cancellationToken
    )
    {
        _EnsureInitialized();

        var count = _storage!.ReceivedMessages.Values.Count(m =>
            string.Equals(m.Origin.Id, messageId, StringComparison.Ordinal)
            && string.Equals(m.Group, group, StringComparison.Ordinal)
        );

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _EnsureInitialized();
        await _initializer!.InitializeAsync(AbortToken);
    }

    private void _EnsureInitialized()
    {
        if (_initializer is not null)
        {
            return;
        }

        // Seed the FakeTimeProvider at the real current UTC instant so legacy tests that mix
        // `DateTime.UtcNow.AddHours(...)` literals with the storage's injected clock stay
        // consistent. The clock-controlled grace test advances the clock explicitly via Advance().
        _fakeTimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.RetryPolicy.MaxPersistedRetries = 4;
            x.FailedMessageExpiredAfter = 3600;
        });
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<TimeProvider>(_fakeTimeProvider);

        var provider = services.BuildServiceProvider();

        var messagingOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();
        _serializer = provider.GetRequiredService<ISerializer>();

        _initializer = new InMemoryStorageInitializer();
        _storage = new InMemoryDataStorage(
            messagingOptions,
            _serializer,
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            _fakeTimeProvider,
            NodeMembership
        );
    }

    #region Data Storage Tests (DataStorageTestsBase parity matrix)

    [Fact]
    public override Task should_initialize_schema() => base.should_initialize_schema();

    [Fact]
    public override Task should_get_table_names() => base.should_get_table_names();

    [Fact]
    public override Task should_store_published_message() => base.should_store_published_message();

    [Fact]
    public override Task should_store_published_message_with_non_numeric_message_id() =>
        base.should_store_published_message_with_non_numeric_message_id();

    [Fact]
    public override Task should_store_received_message() => base.should_store_received_message();

    [Fact]
    public override Task should_store_received_exception_message() => base.should_store_received_exception_message();

    [Fact]
    public override Task should_change_publish_state() => base.should_change_publish_state();

    [Fact]
    public override Task should_change_receive_state() => base.should_change_receive_state();

    [Fact]
    public override Task should_change_publish_state_to_delayed() => base.should_change_publish_state_to_delayed();

    [Fact]
    public override Task should_not_flip_terminal_published_row_back_to_delayed() =>
        base.should_not_flip_terminal_published_row_back_to_delayed();

    [Fact]
    public override Task should_ignore_unknown_storage_ids_when_flushing_delayed_state() =>
        base.should_ignore_unknown_storage_ids_when_flushing_delayed_state();

    [Fact]
    public override Task should_get_published_messages_of_need_retry() =>
        base.should_get_published_messages_of_need_retry();

    [Fact]
    public override Task should_get_received_messages_of_need_retry() =>
        base.should_get_received_messages_of_need_retry();

    [Fact]
    public override Task should_delete_expired_messages() => base.should_delete_expired_messages();

    [Fact]
    public override Task should_not_delete_expired_failed_messages_with_pending_retry() =>
        base.should_not_delete_expired_failed_messages_with_pending_retry();

    [Fact]
    public override Task should_delete_published_message() => base.should_delete_published_message();

    [Fact]
    public override Task should_delete_received_message() => base.should_delete_received_message();

    [Fact]
    public override Task should_get_monitoring_api() => base.should_get_monitoring_api();

    [Fact]
    public override Task should_handle_concurrent_storage_operations() =>
        base.should_handle_concurrent_storage_operations();

    [Fact]
    public override Task should_schedule_messages_of_delayed() => base.should_schedule_messages_of_delayed();

    [Fact]
    public override Task should_store_message_with_transaction() => base.should_store_message_with_transaction();

    [Fact]
    public override Task should_handle_message_state_transitions() => base.should_handle_message_state_transitions();

    [Fact]
    public override Task should_handle_failed_message_state() => base.should_handle_failed_message_state();

    [Fact]
    public override Task should_not_return_published_message_with_failed_status_and_null_next_retry_at() =>
        base.should_not_return_published_message_with_failed_status_and_null_next_retry_at();

    [Fact]
    public override Task should_seal_succeeded_published_message_against_state_change_and_retry_pickup() =>
        base.should_seal_succeeded_published_message_against_state_change_and_retry_pickup();

    [Fact]
    public override Task should_not_return_published_message_with_future_next_retry_at() =>
        base.should_not_return_published_message_with_future_next_retry_at();

    [Fact]
    public override Task should_not_return_received_message_with_failed_status_and_null_next_retry_at() =>
        base.should_not_return_received_message_with_failed_status_and_null_next_retry_at();

    [Fact]
    public override Task should_not_return_received_message_with_future_next_retry_at() =>
        base.should_not_return_received_message_with_future_next_retry_at();

    [Fact]
    public override async Task should_not_return_leased_published_message_until_lease_expires()
    {
        // InMemory storage reads pickup time from the injected FakeTimeProvider. The base test
        // uses `Task.Delay` + wall-clock arithmetic, so we drive the fake clock forward
        // explicitly here to keep storage time and test timestamps in lockstep.
        _EnsureInitialized();
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "leased-published",
            CreateMessage(),
            cancellationToken: AbortToken
        );

        var now = _fakeTimeProvider!.GetUtcNow().UtcDateTime;
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: now.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var leaseWindow = TimeSpan.FromMilliseconds(500);
        var leased = await storage.LeasePublishAsync(storedMessage, now.Add(leaseWindow), AbortToken);

        leased.Should().BeTrue();
        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        _fakeTimeProvider.Advance(leaseWindow + TimeSpan.FromMilliseconds(250));

        (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    [Fact]
    public override Task should_reject_mismatched_original_retries() =>
        base.should_reject_mismatched_original_retries();

    [Fact]
    public override Task should_lease_and_reserve_publish_attempt_in_single_step() =>
        base.should_lease_and_reserve_publish_attempt_in_single_step();

    [Fact]
    public override Task should_reject_lease_and_reserve_with_stale_inline_attempts_token() =>
        base.should_reject_lease_and_reserve_with_stale_inline_attempts_token();

    [Fact]
    public async Task should_reserve_publish_attempt_with_inline_attempt_compare_and_swap()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        var message = await storage.StoreMessageAsync("inline-cas", CreateMessage(), cancellationToken: AbortToken);
        (await storage.LeasePublishAsync(message, DateTime.UtcNow.AddMinutes(1), AbortToken)).Should().BeTrue();
        message.InlineAttempts = 1;

        var reserved = await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 0, AbortToken);
        message.InlineAttempts = 2;
        var staleReservation = await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 0, AbortToken);

        reserved.Should().BeTrue();
        staleReservation.Should().BeFalse();
    }

    [Fact]
    public async Task should_reserve_receive_attempt_with_inline_attempt_compare_and_swap()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        var message = await storage.StoreReceivedMessageAsync("inline-cas", "group", CreateMessage(), AbortToken);
        (await storage.LeaseReceiveAsync(message, DateTime.UtcNow.AddMinutes(1), AbortToken)).Should().BeTrue();
        message.InlineAttempts = 1;

        var reserved = await storage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken);
        message.InlineAttempts = 2;
        var staleReservation = await storage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken);

        reserved.Should().BeTrue();
        staleReservation.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_stale_publish_retry_transition_after_inline_attempt_advances()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        var message = await storage.StoreMessageAsync(
            "inline-transition-cas",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        (await storage.LeasePublishAsync(message, DateTime.UtcNow.AddMinutes(1), AbortToken)).Should().BeTrue();
        message.InlineAttempts = 1;
        (await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 0, AbortToken)).Should().BeTrue();

        message.InlineAttempts = 2;
        var updated = await storage.ChangePublishRetryStateAsync(
            message,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow,
            lockedUntil: null,
            originalRetries: 0,
            originalInlineAttempts: 0,
            cancellationToken: AbortToken
        );

        updated.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_stale_receive_retry_transition_after_inline_attempt_advances()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        var message = await storage.StoreReceivedMessageAsync(
            "inline-transition-cas",
            "group",
            CreateMessage(),
            AbortToken
        );
        (await storage.LeaseReceiveAsync(message, DateTime.UtcNow.AddMinutes(1), AbortToken)).Should().BeTrue();
        message.InlineAttempts = 1;
        (await storage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken)).Should().BeTrue();

        message.InlineAttempts = 2;
        var updated = await storage.ChangeReceiveRetryStateAsync(
            message,
            StatusName.Failed,
            nextRetryAt: DateTime.UtcNow,
            lockedUntil: null,
            originalRetries: 0,
            originalInlineAttempts: 0,
            cancellationToken: AbortToken
        );

        updated.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_attempt_reservation_after_publish_lease_expires()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        var message = await storage.StoreMessageAsync(
            "expired-reservation",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        var lease = TimeSpan.FromSeconds(10);
        (await storage.LeasePublishAsync(message, _fakeTimeProvider!.GetUtcNow().UtcDateTime.Add(lease), AbortToken))
            .Should()
            .BeTrue();
        message.InlineAttempts = 1;
        _fakeTimeProvider.Advance(lease);

        (await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 0, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_attempt_reservation_after_receive_lease_expires()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        var message = await storage.StoreReceivedMessageAsync(
            "expired-reservation",
            "group",
            CreateMessage(),
            AbortToken
        );
        var lease = TimeSpan.FromSeconds(10);
        (await storage.LeaseReceiveAsync(message, _fakeTimeProvider!.GetUtcNow().UtcDateTime.Add(lease), AbortToken))
            .Should()
            .BeTrue();
        message.InlineAttempts = 1;
        _fakeTimeProvider.Advance(lease);

        (await storage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_stale_publish_success_after_replacement_lease_is_acquired()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        NodeMembership.SetIdentity("owner-a");
        var message = await storage.StoreMessageAsync("stale-success", CreateMessage(), cancellationToken: AbortToken);
        var lease = TimeSpan.FromSeconds(10);
        (await storage.LeasePublishAsync(message, _fakeTimeProvider!.GetUtcNow().UtcDateTime.Add(lease), AbortToken))
            .Should()
            .BeTrue();
        message.InlineAttempts = 1;
        (await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 0, AbortToken)).Should().BeTrue();
        var staleLockedUntil = message.LockedUntil;
        var staleOwner = message.Owner;

        _fakeTimeProvider.Advance(lease);
        NodeMembership.SetIdentity("owner-b");
        (await storage.LeasePublishAsync(message, _fakeTimeProvider.GetUtcNow().UtcDateTime.Add(lease), AbortToken))
            .Should()
            .BeTrue();
        message.LockedUntil = staleLockedUntil;
        message.Owner = staleOwner;

        (
            await storage.ChangePublishRetryStateAsync(
                message,
                StatusName.Succeeded,
                null,
                null,
                message.Retries,
                message.InlineAttempts,
                AbortToken
            )
        )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_reject_stale_receive_success_after_replacement_lease_is_acquired()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        NodeMembership.SetIdentity("owner-a");
        var message = await storage.StoreReceivedMessageAsync("stale-success", "group", CreateMessage(), AbortToken);
        var lease = TimeSpan.FromSeconds(10);
        (await storage.LeaseReceiveAsync(message, _fakeTimeProvider!.GetUtcNow().UtcDateTime.Add(lease), AbortToken))
            .Should()
            .BeTrue();
        message.InlineAttempts = 1;
        (await storage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken)).Should().BeTrue();
        var staleLockedUntil = message.LockedUntil;
        var staleOwner = message.Owner;

        _fakeTimeProvider.Advance(lease);
        NodeMembership.SetIdentity("owner-b");
        (await storage.LeaseReceiveAsync(message, _fakeTimeProvider.GetUtcNow().UtcDateTime.Add(lease), AbortToken))
            .Should()
            .BeTrue();
        message.LockedUntil = staleLockedUntil;
        message.Owner = staleOwner;

        (
            await storage.ChangeReceiveRetryStateAsync(
                message,
                StatusName.Succeeded,
                null,
                null,
                message.Retries,
                message.InlineAttempts,
                AbortToken
            )
        )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_preserve_durable_retry_counters_on_expired_lease_redelivery()
    {
        _EnsureInitialized();
        var storage = GetStorage();
        var origin = CreateMessage();
        var message = await storage.StoreReceivedMessageAsync("redelivery-budget", "group", origin, AbortToken);
        (await storage.LeaseReceiveAsync(message, _fakeTimeProvider!.GetUtcNow().UtcDateTime.AddMinutes(1), AbortToken))
            .Should()
            .BeTrue();
        message.InlineAttempts = 1;
        (await storage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken)).Should().BeTrue();
        message.Retries = 1;
        message.InlineAttempts = 0;
        (
            await storage.ChangeReceiveRetryStateAsync(
                message,
                StatusName.Failed,
                _fakeTimeProvider.GetUtcNow().UtcDateTime,
                null,
                originalRetries: 0,
                originalInlineAttempts: 1,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var redelivered = await storage.StoreReceivedMessageAsync("redelivery-budget", "group", origin, AbortToken);

        redelivered.Retries.Should().Be(1);
        redelivered.InlineAttempts.Should().Be(0);
    }

    [Fact]
    public override Task should_report_false_when_received_exception_message_is_already_terminal() =>
        base.should_report_false_when_received_exception_message_is_already_terminal();

    [Fact]
    public override Task should_handle_concurrent_redelivery_storm_on_same_message_id() =>
        base.should_handle_concurrent_redelivery_storm_on_same_message_id();

    [Fact]
    public override Task should_handle_concurrent_first_insert_storm_with_null_and_non_null_group() =>
        base.should_handle_concurrent_first_insert_storm_with_null_and_non_null_group();

    [Fact]
    public override Task should_handle_concurrent_store_received_message_with_same_identity() =>
        base.should_handle_concurrent_store_received_message_with_same_identity();

    [Fact]
    public override Task should_pickup_message_at_max_persisted_retries_and_exclude_above() =>
        base.should_pickup_message_at_max_persisted_retries_and_exclude_above();

    [Fact]
    public override Task should_respect_initial_dispatch_grace() => base.should_respect_initial_dispatch_grace();

    [Fact]
    public override async Task should_not_return_leased_received_message_until_lease_expires()
    {
        // InMemory storage reads pickup time from the injected FakeTimeProvider. The base test
        // uses `Task.Delay` + wall-clock arithmetic, so we drive the fake clock forward
        // explicitly here to keep storage time and test timestamps in lockstep.
        _EnsureInitialized();
        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "leased-received",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        var now = _fakeTimeProvider!.GetUtcNow().UtcDateTime;
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: now.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var leaseWindow = TimeSpan.FromMilliseconds(500);
        var leased = await storage.LeaseReceiveAsync(storedMessage, now.Add(leaseWindow), AbortToken);

        leased.Should().BeTrue();
        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        _fakeTimeProvider.Advance(leaseWindow + TimeSpan.FromMilliseconds(250));

        (await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    [Fact]
    public override Task should_return_unstored_snapshot_when_redelivery_hits_active_receive_lease() =>
        base.should_return_unstored_snapshot_when_redelivery_hits_active_receive_lease();

    [Fact]
    public override Task should_handle_concurrent_state_updates_to_same_row() =>
        base.should_handle_concurrent_state_updates_to_same_row();

    [Fact]
    public override Task should_reclaim_published_retry_row_owned_by_dead_node() =>
        base.should_reclaim_published_retry_row_owned_by_dead_node();

    [Fact]
    public override Task should_reclaim_received_retry_row_owned_by_dead_node() =>
        base.should_reclaim_received_retry_row_owned_by_dead_node();

    [Fact]
    public override Task should_stamp_owner_on_claim() => base.should_stamp_owner_on_claim();

    [Fact]
    public override Task should_not_reclaim_rows_of_live_or_restarted_incarnation() =>
        base.should_not_reclaim_rows_of_live_or_restarted_incarnation();

    [Fact]
    public override Task should_not_reclaim_terminal_rows() => base.should_not_reclaim_terminal_rows();

    [Fact]
    public override Task should_be_inert_when_no_dead_owners_passed() =>
        base.should_be_inert_when_no_dead_owners_passed();

    [Fact]
    public override Task should_not_reclaim_rows_with_null_owner() => base.should_not_reclaim_rows_with_null_owner();

    [Fact]
    public override Task should_reclaim_dead_owner_rows_idempotently() =>
        base.should_reclaim_dead_owner_rows_idempotently();

    [Fact]
    public override Task should_not_reclaim_dead_owner_rows_with_expired_lease() =>
        base.should_not_reclaim_dead_owner_rows_with_expired_lease();

    #endregion

    [Fact]
    public async Task should_respect_configured_retry_batch_size()
    {
        // given
        var storage = _CreateStorage(retryBatchSize: 3);
        var now = _fakeTimeProvider!.GetUtcNow().UtcDateTime;
        var messages = new List<MediumMessage>();

        for (var i = 0; i < 5; i++)
        {
            var stored = await storage.StoreMessageAsync(
                "retry-batch-size",
                CreateMessage(),
                cancellationToken: AbortToken
            );
            stored.Retries = i;

            await storage.ChangePublishStateAsync(
                stored,
                StatusName.Failed,
                nextRetryAt: now.AddSeconds(-1),
                cancellationToken: AbortToken
            );
            messages.Add(stored);
        }

        // when
        var retriable = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();

        // then
        retriable.Should().HaveCount(3);
        retriable
            .Select(message => message.StorageId)
            .Should()
            .BeSubsetOf(messages.Select(message => message.StorageId));
    }

    private InMemoryDataStorage _CreateStorage(int retryBatchSize)
    {
        _fakeTimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.RetryBatchSize = retryBatchSize;
            x.RetryPolicy.MaxPersistedRetries = 4;
            x.FailedMessageExpiredAfter = 3600;
        });
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<TimeProvider>(_fakeTimeProvider);

        var provider = services.BuildServiceProvider();
        _serializer = provider.GetRequiredService<ISerializer>();

        return new InMemoryDataStorage(
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            _serializer,
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            _fakeTimeProvider,
            NodeMembership
        );
    }
}
