// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerDataStorageTests(SqlServerTestFixture fixture) : TestBase
{
    private SqlServerDataStorage _storage = null!;
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
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync();
        _storage = new SqlServerDataStorage(
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            provider.GetRequiredService<IOptions<SqlServerOptions>>(),
            initializer,
            provider.GetRequiredService<ISerializer>(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            _timeProvider,
            new NullNodeMembership(),
            NullLogger<SqlServerDataStorage>.Instance
        );

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("TRUNCATE TABLE messaging.published; TRUNCATE TABLE messaging.received;");
        await base.DisposeAsyncCore();
    }

    #region Message CRUD Tests

    [Fact]
    public async Task should_store_and_retrieve_published_message()
    {
        // given
        var msgId = Guid.NewGuid().ToString("D");
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
        stored.StorageId.Should().NotBe(Guid.Empty);
        stored.Origin.Should().NotBeNull();
        stored.Retries.Should().Be(0);
    }

    [Fact]
    public async Task should_store_published_message_with_maximum_supported_message_id_length()
    {
        // given
        var msgId = new string('m', MessageOptions.MessageIdMaxLength);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, """{"test": "payload"}""");

        // when
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        // then
        stored.Origin.Headers[Headers.MessageId].Should().Be(msgId);
        stored.Origin.Headers[Headers.MessageId].Should().HaveLength(MessageOptions.MessageIdMaxLength);
    }

    [Fact]
    public async Task should_delete_published_message()
    {
        // given
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        // when
        var deleted = await _storage.DeletePublishedMessageAsync(stored.StorageId, AbortToken);

        // then
        deleted.Should().Be(1);
    }

    [Fact]
    public async Task should_return_zero_when_deleting_nonexistent_published_message()
    {
        // when
        var deleted = await _storage.DeletePublishedMessageAsync(Guid.NewGuid(), AbortToken);

        // then
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task should_delete_received_message()
    {
        // given
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);

        // when
        var deleted = await _storage.DeleteReceivedMessageAsync(stored.StorageId, AbortToken);

        // then
        deleted.Should().Be(1);
    }

    #endregion

    #region State Transition Tests

    [Fact]
    public async Task should_change_publish_state_to_succeeded()
    {
        // given
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        // when
        await _storage.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // then - verify via monitoring API
        var monitoringApi = _storage.GetMonitoringApi();
        var retrieved = await monitoringApi.GetPublishedMessageAsync(stored.StorageId, AbortToken);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task should_change_publish_state_to_failed()
    {
        // given
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        // when
        stored.Retries = 3;
        stored.ExpiresAt = _timeProvider.GetUtcNow().AddHours(1);
        await _storage.ChangePublishStateAsync(
            stored,
            StatusName.Failed,
            nextRetryAt: _timeProvider.GetUtcNow().AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // then
        var monitoringApi = _storage.GetMonitoringApi();
        var stats = await monitoringApi.GetStatisticsAsync(AbortToken);
        stats.PublishedFailed.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_change_receive_state_to_succeeded()
    {
        // given
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);
        var stored = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);

        // when
        stored.ExpiresAt = _timeProvider.GetUtcNow().AddHours(1);
        await _storage.ChangeReceiveStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // then
        var monitoringApi = _storage.GetMonitoringApi();
        var stats = await monitoringApi.GetStatisticsAsync(AbortToken);
        stats.ReceivedSucceeded.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_change_publish_state_to_delayed()
    {
        // given
        var msgId1 = Guid.NewGuid().ToString("D");
        var msgId2 = Guid.NewGuid().ToString("D");
        var header1 = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId1 };
        var header2 = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId2 };
        var stored1 = await _storage.StoreMessageAsync("test.name", new Message(header1, null), null, AbortToken);
        var stored2 = await _storage.StoreMessageAsync("test.name", new Message(header2, null), null, AbortToken);

        // when
        await _storage.ChangePublishStateToDelayedAsync([stored1.StorageId, stored2.StorageId], AbortToken);

        // then
        var monitoringApi = _storage.GetMonitoringApi();
        var stats = await monitoringApi.GetStatisticsAsync(AbortToken);
        stats.PublishedDelayed.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_handle_empty_array_for_delayed_state_change()
    {
        // when & then - should not throw
        await _storage.ChangePublishStateToDelayedAsync([], AbortToken);
    }

    #endregion

    #region Retry Message Tests

    [Fact]
    public async Task should_get_published_messages_needing_retry()
    {
        // given - create messages in the past
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        // Store message and change to Failed state
        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().AddHours(1);
        await _storage.ChangePublishStateAsync(
            stored,
            StatusName.Failed,
            nextRetryAt: _timeProvider.GetUtcNow().AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // Advance time so the message is old enough for retry
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var retryMessages = (await _storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();

        // then
        retryMessages.Should().NotBeNull();
        retryMessages.Should().Contain(m => m.StorageId == stored.StorageId);
    }

    [Fact]
    public async Task should_get_received_messages_needing_retry()
    {
        // given
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var stored = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().AddHours(1);
        await _storage.ChangeReceiveStateAsync(
            stored,
            StatusName.Failed,
            nextRetryAt: _timeProvider.GetUtcNow().AddSeconds(-1),
            cancellationToken: AbortToken
        );

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var retryMessages = (await _storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken)).ToList();

        // then
        retryMessages.Should().NotBeNull();
        retryMessages.Should().Contain(m => m.StorageId == stored.StorageId);
    }

    #endregion

    #region Delete Expired Tests

    [Fact]
    public async Task should_delete_expired_succeeded_messages()
    {
        // given
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var stored = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().AddSeconds(-10); // Already expired
        await _storage.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // when
        var deleted = await _storage.DeleteExpiresAsync(
            "messaging.published",
            _timeProvider.GetUtcNow(),
            1000,
            AbortToken
        );

        // then
        deleted.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion
}
