// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
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
    private ILongIdGenerator? _longIdGenerator;
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
            SupportsLocking = true,
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
            string.Equals(m.Origin.GetId(), messageId, StringComparison.Ordinal)
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
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
        services.AddSingleton<TimeProvider>(_fakeTimeProvider);

        var provider = services.BuildServiceProvider();

        var messagingOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();
        _longIdGenerator = provider.GetRequiredService<ILongIdGenerator>();
        _serializer = provider.GetRequiredService<ISerializer>();

        _initializer = new InMemoryStorageInitializer();
        _storage = new InMemoryDataStorage(messagingOptions, _serializer, _longIdGenerator, _fakeTimeProvider);
    }

    #region Data Storage Tests (DataStorageTestsBase parity matrix)

    [Fact]
    public override Task should_initialize_schema() => base.should_initialize_schema();

    // InMemoryStorageInitializer.GetLockTableName() returns string.Empty by design — InMemory has
    // no lock table (locks live in InMemoryDataStorage.Locks). The base test asserts non-null/empty
    // for all three table-name accessors, which is a SQL-storage assumption. Skipped here, not a
    // contract violation.
    [Fact(Skip = "InMemory has no lock table — GetLockTableName() returns string.Empty by design")]
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
    public override Task should_get_published_messages_of_need_retry() =>
        base.should_get_published_messages_of_need_retry();

    [Fact]
    public override Task should_get_received_messages_of_need_retry() =>
        base.should_get_received_messages_of_need_retry();

    [Fact]
    public override Task should_acquire_lock() => base.should_acquire_lock();

    [Fact]
    public override Task should_not_acquire_lock_when_already_held() =>
        base.should_not_acquire_lock_when_already_held();

    [Fact]
    public override Task should_release_lock() => base.should_release_lock();

    [Fact]
    public override Task should_renew_lock() => base.should_renew_lock();

    [Fact]
    public override Task should_delete_expired_messages() => base.should_delete_expired_messages();

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
    public override Task should_handle_concurrent_state_updates_to_same_row() =>
        base.should_handle_concurrent_state_updates_to_same_row();

    #endregion
}
