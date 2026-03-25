// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlMonitoringTest(PostgreSqlTestFixture fixture) : TestBase
{
    private IDataStorage? _storage;
    private IStorageInitializer? _initializer;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _EnsureInitialized();
        await _initializer!.InitializeAsync(AbortToken);

        // Clean tables before each test
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await connection.ExecuteAsync("""
            TRUNCATE TABLE messaging.published;
            TRUNCATE TABLE messaging.received;
            UPDATE messaging.lock SET "Instance"='', "LastLockTime"='0001-01-01 00:00:00+00';
            """);
    }

    [Fact]
    public async Task should_return_correct_statistics()
    {
        // given — seed messages with different statuses
        var storage = _storage!;
        var msg1 = _CreateMessage();
        var msg2 = _CreateMessage();
        var msg3 = _CreateMessage();

        var stored1 = await storage.StoreMessageAsync("stats-test", msg1, cancellationToken: AbortToken);
        var stored2 = await storage.StoreMessageAsync("stats-test", msg2, cancellationToken: AbortToken);
        var stored3 = await storage.StoreMessageAsync("stats-test", msg3, cancellationToken: AbortToken);

        await storage.ChangePublishStateAsync(stored1, StatusName.Succeeded, cancellationToken: AbortToken);
        await storage.ChangePublishStateAsync(stored2, StatusName.Failed, cancellationToken: AbortToken);

        // when
        var monitoringApi = storage.GetMonitoringApi();
        var stats = await monitoringApi.GetStatisticsAsync(AbortToken);

        // then
        stats.PublishedSucceeded.Should().Be(1);
        stats.PublishedFailed.Should().Be(1);
        stats.ReceivedSucceeded.Should().Be(0);
        stats.ReceivedFailed.Should().Be(0);
        stats.PublishedDelayed.Should().Be(0);
    }

    [Fact]
    public async Task should_return_correct_count_methods()
    {
        // given
        var storage = _storage!;
        var msg1 = _CreateMessage();
        var msg2 = _CreateMessage();

        var stored1 = await storage.StoreMessageAsync("count-test", msg1, cancellationToken: AbortToken);
        var stored2 = await storage.StoreMessageAsync("count-test", msg2, cancellationToken: AbortToken);

        await storage.ChangePublishStateAsync(stored1, StatusName.Succeeded, cancellationToken: AbortToken);
        await storage.ChangePublishStateAsync(stored2, StatusName.Failed, cancellationToken: AbortToken);

        // when
        var monitoringApi = storage.GetMonitoringApi();
        var succeededCount = await monitoringApi.PublishedSucceededCount(AbortToken);
        var failedCount = await monitoringApi.PublishedFailedCount(AbortToken);

        // then
        succeededCount.Should().Be(1);
        failedCount.Should().Be(1);
    }

    [Fact]
    public async Task should_return_monitoring_api_of_correct_type()
    {
        var monitoringApi = _storage!.GetMonitoringApi();
        monitoringApi.Should().BeOfType<PostgreSqlMonitoringApi>();
    }

    [Fact]
    public async Task should_get_published_message_by_id()
    {
        // given
        var storage = _storage!;
        var msg = _CreateMessage();
        var stored = await storage.StoreMessageAsync("get-by-id-test", msg, cancellationToken: AbortToken);

        // when
        var monitoringApi = storage.GetMonitoringApi();
        var retrieved = await monitoringApi.GetPublishedMessageAsync(stored.StorageId, AbortToken);

        // then
        retrieved.Should().NotBeNull();
        retrieved!.StorageId.Should().Be(stored.StorageId);
        retrieved.Content.Should().Be(stored.Content);
    }

    [Fact]
    public async Task should_get_received_message_by_id()
    {
        // given
        var storage = _storage!;
        var msg = _CreateMessage();
        var stored = await storage.StoreReceivedMessageAsync("get-received-test", "test-group", msg, AbortToken);

        // when
        var monitoringApi = storage.GetMonitoringApi();
        var retrieved = await monitoringApi.GetReceivedMessageAsync(stored.StorageId, AbortToken);

        // then
        retrieved.Should().NotBeNull();
        retrieved!.StorageId.Should().Be(stored.StorageId);
    }

    [Fact]
    public async Task should_return_null_for_nonexistent_message()
    {
        var monitoringApi = _storage!.GetMonitoringApi();
        var result = await monitoringApi.GetPublishedMessageAsync(999_999_999, AbortToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_paginate_published_messages()
    {
        // given — seed 3 messages
        var storage = _storage!;
        for (var i = 0; i < 3; i++)
        {
            await storage.StoreMessageAsync("page-test", _CreateMessage(), cancellationToken: AbortToken);
        }

        // when — page size 2, page 0
        var monitoringApi = storage.GetMonitoringApi();
        var page0 = await monitoringApi.GetMessagesAsync(
            new MessageQuery { MessageType = MessageType.Publish, CurrentPage = 0, PageSize = 2 },
            AbortToken
        );

        // then
        page0.Items.Should().HaveCount(2);
        page0.TotalItems.Should().Be(3);

        // when — page 1
        var page1 = await monitoringApi.GetMessagesAsync(
            new MessageQuery { MessageType = MessageType.Publish, CurrentPage = 1, PageSize = 2 },
            AbortToken
        );

        page1.Items.Should().HaveCount(1);
        page1.TotalItems.Should().Be(3);
    }

    [Fact]
    public async Task should_filter_messages_by_status()
    {
        // given
        var storage = _storage!;
        var msg1 = _CreateMessage();
        var msg2 = _CreateMessage();
        var stored1 = await storage.StoreMessageAsync("filter-test", msg1, cancellationToken: AbortToken);
        await storage.StoreMessageAsync("filter-test", msg2, cancellationToken: AbortToken);
        await storage.ChangePublishStateAsync(stored1, StatusName.Succeeded, cancellationToken: AbortToken);

        // when
        var monitoringApi = storage.GetMonitoringApi();
        var result = await monitoringApi.GetMessagesAsync(
            new MessageQuery { MessageType = MessageType.Publish, StatusName = "Succeeded", CurrentPage = 0, PageSize = 10 },
            AbortToken
        );

        // then
        result.Items.Should().HaveCount(1);
        result.Items[0].StatusName.Should().Be("Succeeded");
    }

    [Fact]
    public async Task should_return_hourly_succeeded_jobs()
    {
        // given — seed a succeeded message
        var storage = _storage!;
        var msg = _CreateMessage();
        var stored = await storage.StoreMessageAsync("hourly-test", msg, cancellationToken: AbortToken);
        await storage.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // when
        var monitoringApi = storage.GetMonitoringApi();
        var hourly = await monitoringApi.HourlySucceededJobs(MessageType.Publish, AbortToken);

        // then — should have 24 hour buckets, at least one with count > 0
        hourly.Should().HaveCount(24);
        hourly.Values.Sum().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_return_hourly_failed_jobs()
    {
        // given
        var storage = _storage!;
        var msg = _CreateMessage();
        var stored = await storage.StoreMessageAsync("hourly-fail-test", msg, cancellationToken: AbortToken);
        await storage.ChangePublishStateAsync(stored, StatusName.Failed, cancellationToken: AbortToken);

        // when
        var monitoringApi = storage.GetMonitoringApi();
        var hourly = await monitoringApi.HourlyFailedJobs(MessageType.Publish, AbortToken);

        // then
        hourly.Should().HaveCount(24);
        hourly.Values.Sum().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_store_message_within_npgsql_transaction()
    {
        // given
        var storage = _storage!;
        var msg = _CreateMessage();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);

        // when
        var result = await storage.StoreMessageAsync("tx-test", msg, transaction, AbortToken);

        // then — message is visible within the transaction
        result.Should().NotBeNull();
        result.StorageId.Should().BeGreaterThan(0);

        await transaction.CommitAsync(AbortToken);

        // verify message persisted after commit
        var monitoringApi = storage.GetMonitoringApi();
        var retrieved = await monitoringApi.GetPublishedMessageAsync(result.StorageId, AbortToken);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task should_rollback_transaction_and_not_persist_message()
    {
        // given
        var storage = _storage!;
        var msg = _CreateMessage();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);

        // when — store then rollback
        var result = await storage.StoreMessageAsync("rollback-test", msg, transaction, AbortToken);
        await transaction.RollbackAsync(AbortToken);

        // then — message should not be persisted
        var monitoringApi = storage.GetMonitoringApi();
        var retrieved = await monitoringApi.GetPublishedMessageAsync(result.StorageId, AbortToken);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_for_unsupported_transaction_type()
    {
        // given
        var storage = _storage!;
        var msg = _CreateMessage();
        var unsupportedTransaction = new object();

        // when
        var act = async () => await storage.StoreMessageAsync("bad-tx", msg, unsupportedTransaction, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported transaction type*");
    }

    [Fact]
    public async Task should_return_empty_page_when_no_messages_match()
    {
        // given — empty table (truncated in InitializeAsync)
        var monitoringApi = _storage!.GetMonitoringApi();

        // when
        var page = await monitoringApi.GetMessagesAsync(
            new MessageQuery { MessageType = MessageType.Publish, CurrentPage = 0, PageSize = 10 },
            AbortToken
        );

        // then
        page.Items.Should().BeEmpty();
        page.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task should_renew_lock_and_advance_last_lock_time()
    {
        // given
        var storage = _storage!;
        const string key = "publish_retry_v1";
        var instance = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromSeconds(30);

        await storage.AcquireLockAsync(key, ttl, instance, AbortToken);

        // read LastLockTime before renewal
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var before = await connection.QueryFirstAsync<DateTime>(
            "SELECT \"LastLockTime\" FROM messaging.lock WHERE \"Key\" = @Key",
            new { Key = key }
        );

        // when
        await storage.RenewLockAsync(key, ttl, instance, AbortToken);

        // then — LastLockTime should have advanced
        var after = await connection.QueryFirstAsync<DateTime>(
            "SELECT \"LastLockTime\" FROM messaging.lock WHERE \"Key\" = @Key",
            new { Key = key }
        );
        after.Should().BeAfter(before);
    }

    private static long _messageIdCounter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 20;

    private static Message _CreateMessage()
    {
        var id = $"msg-{Interlocked.Increment(ref _messageIdCounter)}";
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { Headless.Messaging.Headers.MessageId, id },
            { Headless.Messaging.Headers.MessageName, "TestMessage" },
            { Headless.Messaging.Headers.SentTime, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
        };
        return new Message(headers, new { Data = "test" });
    }

    private void _EnsureInitialized()
    {
        if (_initializer is not null) return;

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.FailedRetryCount = 5;
            x.FailedMessageExpiredAfter = 3600;
            x.UseStorageLock = true;
        });
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
        services.AddSingleton(TimeProvider.System);

        var provider = services.BuildServiceProvider();
        var postgreSqlOptions = provider.GetRequiredService<IOptions<PostgreSqlOptions>>();
        var messagingOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();

        _initializer = new PostgreSqlStorageInitializer(
            NullLogger<PostgreSqlStorageInitializer>.Instance,
            postgreSqlOptions,
            messagingOptions
        );

        _storage = new PostgreSqlDataStorage(
            postgreSqlOptions,
            messagingOptions,
            _initializer,
            provider.GetRequiredService<ISerializer>(),
            provider.GetRequiredService<ILongIdGenerator>(),
            TimeProvider.System
        );
    }
}
