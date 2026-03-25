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
