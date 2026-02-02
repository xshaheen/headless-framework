// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerDataStorageTests(SqlServerTestFixture fixture) : TestBase
{
    private SqlServerDataStorage _storage = null!;
    private ILongIdGenerator _longIdGenerator = null!;
    private FakeTimeProvider _timeProvider = null!;

    public override async ValueTask InitializeAsync()
    {
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<SqlServerOptions>(x =>
        {
            x.ConnectionString = fixture.ConnectionString;
            x.Schema = "messaging";
            x.Version = "v1"; // Must match MessagingOptions.Version for retry queries
        });
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.UseStorageLock = true;
        });
        services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync();

        _longIdGenerator = provider.GetRequiredService<ILongIdGenerator>();
        _storage = new SqlServerDataStorage(
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            provider.GetRequiredService<IOptions<SqlServerOptions>>(),
            initializer,
            provider.GetRequiredService<ISerializer>(),
            _longIdGenerator,
            _timeProvider
        );

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "TRUNCATE TABLE messaging.published; TRUNCATE TABLE messaging.received; DELETE FROM messaging.Lock;"
        );
        await base.DisposeAsyncCore();
    }

    #region Lock Tests

    [Fact]
    public async Task should_acquire_lock_when_not_held()
    {
        // given
        var key = "test_lock_" + Guid.NewGuid().ToString("N");
        const string instance = "instance1";
        var ttl = TimeSpan.FromMinutes(5);

        // Insert the lock key first
        await _InsertLockKey(key);

        // when
        var acquired = await _storage.AcquireLockAsync(key, ttl, instance, AbortToken);

        // then
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_acquire_lock_when_already_held()
    {
        // given
        var key = "test_lock_" + Guid.NewGuid().ToString("N");
        const string instance1 = "instance1";
        const string instance2 = "instance2";
        var ttl = TimeSpan.FromMinutes(5);

        await _InsertLockKey(key);

        // First instance acquires lock
        await _storage.AcquireLockAsync(key, ttl, instance1, AbortToken);

        // when - second instance tries to acquire
        var acquired = await _storage.AcquireLockAsync(key, ttl, instance2, AbortToken);

        // then
        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task should_acquire_lock_after_ttl_expires()
    {
        // given
        var key = "test_lock_" + Guid.NewGuid().ToString("N");
        const string instance1 = "instance1";
        const string instance2 = "instance2";
        var ttl = TimeSpan.FromSeconds(1);

        await _InsertLockKey(key);

        // First instance acquires lock
        await _storage.AcquireLockAsync(key, ttl, instance1, AbortToken);

        // Advance time past TTL
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // when - second instance tries to acquire after TTL
        var acquired = await _storage.AcquireLockAsync(key, ttl, instance2, AbortToken);

        // then
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_release_lock()
    {
        // given
        var key = "test_lock_" + Guid.NewGuid().ToString("N");
        const string instance = "instance1";
        var ttl = TimeSpan.FromMinutes(5);

        await _InsertLockKey(key);
        await _storage.AcquireLockAsync(key, ttl, instance, AbortToken);

        // when
        await _storage.ReleaseLockAsync(key, instance, AbortToken);

        // then - another instance should now be able to acquire
        var acquired = await _storage.AcquireLockAsync(key, ttl, "instance2", AbortToken);
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_release_lock_held_by_different_instance()
    {
        // given
        var key = "test_lock_" + Guid.NewGuid().ToString("N");
        const string instance1 = "instance1";
        const string instance2 = "instance2";
        var ttl = TimeSpan.FromMinutes(5);

        await _InsertLockKey(key);
        await _storage.AcquireLockAsync(key, ttl, instance1, AbortToken);

        // when - different instance tries to release
        await _storage.ReleaseLockAsync(key, instance2, AbortToken);

        // then - lock should still be held
        var acquired = await _storage.AcquireLockAsync(key, ttl, "instance3", AbortToken);
        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task should_renew_lock()
    {
        // given
        var key = "test_lock_" + Guid.NewGuid().ToString("N");
        const string instance = "instance1";
        var ttl = TimeSpan.FromSeconds(5);

        await _InsertLockKey(key);
        await _storage.AcquireLockAsync(key, ttl, instance, AbortToken);

        // Advance time but within original TTL
        _timeProvider.Advance(TimeSpan.FromSeconds(3));

        // when - renew the lock
        await _storage.RenewLockAsync(key, ttl, instance, AbortToken);

        // Advance time past original TTL but within renewed TTL
        _timeProvider.Advance(TimeSpan.FromSeconds(4));

        // then - another instance should NOT be able to acquire (lock was renewed)
        var acquired = await _storage.AcquireLockAsync(key, ttl, "instance2", AbortToken);
        acquired.Should().BeFalse();
    }

    #endregion

    #region Message CRUD Tests

    [Fact]
    public async Task should_store_and_retrieve_published_message()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = msgId,
            ["custom-header"] = "test-value",
        };
        var message = new Message(header, """{"test": "payload"}""");

        // when
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        // then
        stored.Should().NotBeNull();
        stored.DbId.Should().NotBeNullOrEmpty();
        stored.Origin.Should().NotBeNull();
        stored.Retries.Should().Be(0);
    }

    [Fact]
    public async Task should_delete_published_message()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        // when
        var deleted = await _storage.DeletePublishedMessageAsync(
            long.Parse(stored.DbId, CultureInfo.InvariantCulture),
            AbortToken
        );

        // then
        deleted.Should().Be(1);
    }

    [Fact]
    public async Task should_return_zero_when_deleting_nonexistent_published_message()
    {
        // when
        var deleted = await _storage.DeletePublishedMessageAsync(999999999L, AbortToken);

        // then
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task should_delete_received_message()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);

        // when
        var deleted = await _storage.DeleteReceivedMessageAsync(
            long.Parse(stored.DbId, CultureInfo.InvariantCulture),
            AbortToken
        );

        // then
        deleted.Should().Be(1);
    }

    #endregion

    #region State Transition Tests

    [Fact]
    public async Task should_change_publish_state_to_succeeded()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        // when
        await _storage.ChangePublishStateAsync(stored, StatusName.Succeeded, null, AbortToken);

        // then - verify via monitoring API
        var monitoringApi = _storage.GetMonitoringApi();
        var retrieved = await monitoringApi.GetPublishedMessageAsync(
            long.Parse(stored.DbId, CultureInfo.InvariantCulture),
            AbortToken
        );
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task should_change_publish_state_to_failed()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        // when
        stored.Retries = 3;
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
        await _storage.ChangePublishStateAsync(stored, StatusName.Failed, null, AbortToken);

        // then
        var monitoringApi = _storage.GetMonitoringApi();
        var stats = await monitoringApi.GetStatisticsAsync(AbortToken);
        stats.PublishedFailed.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_change_receive_state_to_succeeded()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);

        // when
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
        await _storage.ChangeReceiveStateAsync(stored, StatusName.Succeeded, AbortToken);

        // then
        var monitoringApi = _storage.GetMonitoringApi();
        var stats = await monitoringApi.GetStatisticsAsync(AbortToken);
        stats.ReceivedSucceeded.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_change_publish_state_to_delayed()
    {
        // given
        var msgId1 = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var msgId2 = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header1 = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId1 };
        var header2 = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId2 };
        var stored1 = await _storage.StoreMessageAsync("test.name", new Message(header1, null), null, AbortToken);
        var stored2 = await _storage.StoreMessageAsync("test.name", new Message(header2, null), null, AbortToken);

        // when
        await _storage.ChangePublishStateToDelayedAsync([stored1.DbId, stored2.DbId], AbortToken);

        // then
        var monitoringApi = _storage.GetMonitoringApi();
        var stats = await monitoringApi.GetStatisticsAsync(AbortToken);
        stats.PublishedDelayed.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_handle_empty_array_for_delayed_state_change()
    {
        // when / then - should not throw
        await _storage.ChangePublishStateToDelayedAsync([], AbortToken);
    }

    #endregion

    #region Retry Message Tests

    [Fact]
    public async Task should_get_published_messages_needing_retry()
    {
        // given - create messages in the past
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        // Store message and change to Failed state
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
        await _storage.ChangePublishStateAsync(stored, StatusName.Failed, null, AbortToken);

        // Advance time so the message is old enough for retry
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var retryMessages = await _storage.GetPublishedMessagesOfNeedRetry(TimeSpan.FromMinutes(4), AbortToken);

        // then
        retryMessages.Should().NotBeNull();
        retryMessages.Should().Contain(m => m.DbId == stored.DbId);
    }

    [Fact]
    public async Task should_get_received_messages_needing_retry()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var stored = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
        await _storage.ChangeReceiveStateAsync(stored, StatusName.Failed, AbortToken);

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var retryMessages = await _storage.GetReceivedMessagesOfNeedRetry(TimeSpan.FromMinutes(4), AbortToken);

        // then
        retryMessages.Should().NotBeNull();
        retryMessages.Should().Contain(m => m.DbId == stored.DbId);
    }

    #endregion

    #region Delete Expired Tests

    [Fact]
    public async Task should_delete_expired_succeeded_messages()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(-10); // Already expired
        await _storage.ChangePublishStateAsync(stored, StatusName.Succeeded, null, AbortToken);

        // when
        var deleted = await _storage.DeleteExpiresAsync(
            "messaging.published",
            _timeProvider.GetUtcNow().UtcDateTime,
            1000,
            AbortToken
        );

        // then
        deleted.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    private async Task _InsertLockKey(string key)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO messaging.Lock ([Key], [Instance], [LastLockTime]) VALUES (@Key, '', @LastLockTime)",
            new { Key = key, LastLockTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
    }
}
