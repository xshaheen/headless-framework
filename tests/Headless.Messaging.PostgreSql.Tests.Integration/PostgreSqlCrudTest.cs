// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlCrudTest(PostgreSqlTestFixture fixture) : TestBase
{
    private PostgreSqlDataStorage _storage = null!;
    private ILongIdGenerator _longIdGenerator = null!;

    public override async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.FailedRetryCount = 5;
            x.FailedMessageExpiredAfter = 3600;
        });
        services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
        services.AddSingleton(TimeProvider.System);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync();

        _longIdGenerator = provider.GetRequiredService<ILongIdGenerator>();
        _storage = new PostgreSqlDataStorage(
            provider.GetRequiredService<IOptions<PostgreSqlOptions>>(),
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            initializer,
            provider.GetRequiredService<ISerializer>(),
            _longIdGenerator,
            TimeProvider.System
        );

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("TRUNCATE TABLE messaging.published; TRUNCATE TABLE messaging.received;");
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_delete_published_message()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, new { Data = "test" });
        var stored = await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);
        var id = long.Parse(stored.DbId, CultureInfo.InvariantCulture);

        // when
        var deleted = await _storage.DeletePublishedMessageAsync(id, AbortToken);

        // then
        deleted.Should().Be(1);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var count = await connection.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM messaging.published WHERE \"Id\"=@Id",
            new { Id = id }
        );
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_delete_received_message()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = msgId,
            [Headers.MessageName] = "test.topic",
        };
        var message = new Message(header, new { Data = "test" });
        var stored = await _storage.StoreReceivedMessageAsync("test.topic", "test.group", message, AbortToken);
        var id = long.Parse(stored.DbId, CultureInfo.InvariantCulture);

        // when
        var deleted = await _storage.DeleteReceivedMessageAsync(id, AbortToken);

        // then
        deleted.Should().Be(1);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var count = await connection.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM messaging.received WHERE \"Id\"=@Id",
            new { Id = id }
        );
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_return_zero_when_deleting_nonexistent_published_message()
    {
        // given
        var nonExistentId = 999999999L;

        // when
        var deleted = await _storage.DeletePublishedMessageAsync(nonExistentId, AbortToken);

        // then
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task should_return_zero_when_deleting_nonexistent_received_message()
    {
        // given
        var nonExistentId = 999999999L;

        // when
        var deleted = await _storage.DeleteReceivedMessageAsync(nonExistentId, AbortToken);

        // then
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task should_delete_expired_messages()
    {
        // given - create expired message
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var expiredTime = DateTime.UtcNow.AddDays(-1);
        var id = _longIdGenerator.Create();
        await connection.ExecuteAsync(
            """
            INSERT INTO messaging.published ("Id","Version","Name","Content","Retries","Added","ExpiresAt","StatusName")
            VALUES (@Id,'v1','test.topic','{}',0,@Added,@ExpiresAt,'Succeeded')
            """,
            new
            {
                Id = id,
                Added = expiredTime,
                ExpiresAt = expiredTime,
            }
        );

        // when
        var initializer = new PostgreSqlStorageInitializer(
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<PostgreSqlStorageInitializer>>(),
            Microsoft.Extensions.Options.Options.Create(
                new PostgreSqlOptions { ConnectionString = fixture.ConnectionString }
            ),
            Microsoft.Extensions.Options.Options.Create(new MessagingOptions { Version = "v1" })
        );
        var tableName = initializer.GetPublishedTableName();
        var deleted = await _storage.DeleteExpiresAsync(tableName, DateTime.UtcNow, cancellationToken: AbortToken);

        // then
        deleted.Should().Be(1);
    }

    [Fact]
    public async Task should_not_delete_non_expired_messages()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, new { Data = "test" });
        var stored = await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);

        // Set message to succeeded with future expiry
        stored.ExpiresAt = DateTime.UtcNow.AddDays(1);
        await _storage.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // when
        var initializer = new PostgreSqlStorageInitializer(
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<PostgreSqlStorageInitializer>>(),
            Microsoft.Extensions.Options.Options.Create(
                new PostgreSqlOptions { ConnectionString = fixture.ConnectionString }
            ),
            Microsoft.Extensions.Options.Options.Create(new MessagingOptions { Version = "v1" })
        );
        var tableName = initializer.GetPublishedTableName();
        var deleted = await _storage.DeleteExpiresAsync(tableName, DateTime.UtcNow, cancellationToken: AbortToken);

        // then
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task should_get_published_messages_needing_retry()
    {
        // given - create a failed message that needs retry
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var addedTime = DateTime.UtcNow.AddMinutes(-5);
        var id = _longIdGenerator.Create();
        var content = "{\"Headers\":{\"msg-id\":\"" + id.ToString(CultureInfo.InvariantCulture) + "\"},\"Value\":null}";
        await connection.ExecuteAsync(
            """
            INSERT INTO messaging.published ("Id","Version","Name","Content","Retries","Added","ExpiresAt","StatusName")
            VALUES (@Id,'v1','test.topic',@Content,0,@Added,NULL,'Failed')
            """,
            new
            {
                Id = id,
                Content = content,
                Added = addedTime,
            }
        );

        // when
        var messages = await _storage.GetPublishedMessagesOfNeedRetry(TimeSpan.FromMinutes(4), AbortToken);

        // then
        messages.Should().NotBeEmpty();
        messages.Should().Contain(m => m.DbId == id.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task should_get_received_messages_needing_retry()
    {
        // given - create a failed received message that needs retry
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var addedTime = DateTime.UtcNow.AddMinutes(-5);
        var id = _longIdGenerator.Create();
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var content = "{\"Headers\":{\"msg-id\":\"" + msgId + "\"},\"Value\":null}";
        await connection.ExecuteAsync(
            """
            INSERT INTO messaging.received ("Id","Version","Name","Group","Content","Retries","Added","ExpiresAt","StatusName","MessageId")
            VALUES (@Id,'v1','test.topic','test.group',@Content,0,@Added,NULL,'Failed',@MessageId)
            """,
            new
            {
                Id = id,
                Content = content,
                Added = addedTime,
                MessageId = msgId,
            }
        );

        // when
        var messages = await _storage.GetReceivedMessagesOfNeedRetry(TimeSpan.FromMinutes(4), AbortToken);

        // then
        messages.Should().NotBeEmpty();
        messages.Should().Contain(m => m.DbId == id.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task should_not_get_messages_with_max_retries()
    {
        // given - create a failed message with max retries
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var addedTime = DateTime.UtcNow.AddMinutes(-5);
        var id = _longIdGenerator.Create();
        var content = "{\"Headers\":{\"msg-id\":\"" + id.ToString(CultureInfo.InvariantCulture) + "\"},\"Value\":null}";
        await connection.ExecuteAsync(
            """
            INSERT INTO messaging.published ("Id","Version","Name","Content","Retries","Added","ExpiresAt","StatusName")
            VALUES (@Id,'v1','test.topic',@Content,10,@Added,NULL,'Failed')
            """,
            new
            {
                Id = id,
                Content = content,
                Added = addedTime,
            }
        );

        // when
        var messages = await _storage.GetPublishedMessagesOfNeedRetry(TimeSpan.FromMinutes(4), AbortToken);

        // then
        messages.Should().NotContain(m => m.DbId == id.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task should_store_received_exception_message_with_failed_status()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var content = "{\"Headers\":{\"msg-id\":\"" + msgId + "\"},\"Value\":null}";

        // when
        await _storage.StoreReceivedExceptionMessageAsync("test.topic", "test.group", content, AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var status = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT \"StatusName\" FROM messaging.received WHERE \"MessageId\"=@MessageId",
            new { MessageId = msgId }
        );
        status.Should().Be(nameof(StatusName.Failed));
    }

    [Fact]
    public async Task should_get_monitoring_api()
    {
        // when
        var monitoringApi = _storage.GetMonitoringApi();

        // then
        monitoringApi.Should().NotBeNull();
        monitoringApi.Should().BeOfType<PostgreSqlMonitoringApi>();
    }
}
