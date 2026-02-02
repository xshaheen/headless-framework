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
public sealed class PostgreSqlMessageStateTest(PostgreSqlTestFixture fixture) : TestBase
{
    private PostgreSqlDataStorage _storage = null!;
    private ILongIdGenerator _longIdGenerator = null!;

    public override async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingOptions>(x => x.Version = "v1");
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
        try
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("TRUNCATE TABLE messaging.published; TRUNCATE TABLE messaging.received;");
        }
        catch (PostgresException)
        {
            // Schema may not exist if test failed before initialization
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_store_published_message_with_scheduled_status()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, new { Data = "test" });

        // when
        var stored = await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var status = await connection.QueryFirstAsync<string>(
            "SELECT \"StatusName\" FROM messaging.published WHERE \"Id\"=@Id",
            new { Id = long.Parse(stored.DbId, CultureInfo.InvariantCulture) }
        );
        status.Should().Be(nameof(StatusName.Scheduled));
    }

    [Fact]
    public async Task should_transition_published_message_to_succeeded()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, new { Data = "test" });
        var stored = await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);

        // when
        await _storage.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var status = await connection.QueryFirstAsync<string>(
            "SELECT \"StatusName\" FROM messaging.published WHERE \"Id\"=@Id",
            new { Id = long.Parse(stored.DbId, CultureInfo.InvariantCulture) }
        );
        status.Should().Be(nameof(StatusName.Succeeded));
    }

    [Fact]
    public async Task should_transition_published_message_to_failed()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, new { Data = "test" });
        var stored = await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);

        // when
        await _storage.ChangePublishStateAsync(stored, StatusName.Failed, cancellationToken: AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var status = await connection.QueryFirstAsync<string>(
            "SELECT \"StatusName\" FROM messaging.published WHERE \"Id\"=@Id",
            new { Id = long.Parse(stored.DbId, CultureInfo.InvariantCulture) }
        );
        status.Should().Be(nameof(StatusName.Failed));
    }

    [Fact]
    public async Task should_store_received_message_with_scheduled_status()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = msgId,
            [Headers.MessageName] = "test.topic",
        };
        var message = new Message(header, new { Data = "test" });

        // when
        var stored = await _storage.StoreReceivedMessageAsync("test.topic", "test.group", message, AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var status = await connection.QueryFirstAsync<string>(
            "SELECT \"StatusName\" FROM messaging.received WHERE \"Id\"=@Id",
            new { Id = long.Parse(stored.DbId, CultureInfo.InvariantCulture) }
        );
        status.Should().Be(nameof(StatusName.Scheduled));
    }

    [Fact]
    public async Task should_transition_received_message_to_succeeded()
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

        // when
        await _storage.ChangeReceiveStateAsync(stored, StatusName.Succeeded, AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var status = await connection.QueryFirstAsync<string>(
            "SELECT \"StatusName\" FROM messaging.received WHERE \"Id\"=@Id",
            new { Id = long.Parse(stored.DbId, CultureInfo.InvariantCulture) }
        );
        status.Should().Be(nameof(StatusName.Succeeded));
    }

    [Fact]
    public async Task should_transition_received_message_to_failed()
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

        // when
        await _storage.ChangeReceiveStateAsync(stored, StatusName.Failed, AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var status = await connection.QueryFirstAsync<string>(
            "SELECT \"StatusName\" FROM messaging.received WHERE \"Id\"=@Id",
            new { Id = long.Parse(stored.DbId, CultureInfo.InvariantCulture) }
        );
        status.Should().Be(nameof(StatusName.Failed));
    }

    [Fact]
    public async Task should_update_retries_on_state_change()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, new { Data = "test" });
        var stored = await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);
        stored.Retries = 3;

        // when
        await _storage.ChangePublishStateAsync(stored, StatusName.Scheduled, cancellationToken: AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var retries = await connection.QueryFirstAsync<int>(
            "SELECT \"Retries\" FROM messaging.published WHERE \"Id\"=@Id",
            new { Id = long.Parse(stored.DbId, CultureInfo.InvariantCulture) }
        );
        retries.Should().Be(3);
    }

    [Fact]
    public async Task should_update_expires_at_on_state_change()
    {
        // given
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, new { Data = "test" });
        var stored = await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);
        var expiresAt = DateTime.UtcNow.AddHours(1);
        stored.ExpiresAt = expiresAt;

        // when
        await _storage.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var dbExpiresAt = await connection.QueryFirstAsync<DateTime?>(
            "SELECT \"ExpiresAt\" FROM messaging.published WHERE \"Id\"=@Id",
            new { Id = long.Parse(stored.DbId, CultureInfo.InvariantCulture) }
        );
        dbExpiresAt.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_change_multiple_messages_to_delayed_state()
    {
        // given
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
            var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
            var message = new Message(header, new { Data = $"test{i}" });
            var stored = await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);
            ids.Add(stored.DbId);
        }

        // when
        await _storage.ChangePublishStateToDelayedAsync([.. ids], AbortToken);

        // then
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var statuses = await connection.QueryAsync<string>(
            "SELECT \"StatusName\" FROM messaging.published WHERE \"Id\" = ANY(@Ids)",
            new { Ids = ids.Select(id => long.Parse(id, CultureInfo.InvariantCulture)).ToArray() }
        );
        statuses.Should().AllBe(nameof(StatusName.Delayed));
    }

    [Fact]
    public async Task should_not_fail_when_changing_empty_array_to_delayed()
    {
        // when
        var act = async () => await _storage.ChangePublishStateToDelayedAsync([], AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }
}
