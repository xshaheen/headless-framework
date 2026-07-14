// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlStorageConnectionTest(PostgreSqlTestFixture fixture) : TestBase
{
    private PostgreSqlDataStorage _storage = null!;

    public override async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync();
        _storage = new PostgreSqlDataStorage(
            provider.GetRequiredService<IOptions<PostgreSqlOptions>>(),
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            initializer,
            provider.GetRequiredService<ISerializer>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            TimeProvider.System,
            new NullNodeMembership(),
            NullLogger<PostgreSqlDataStorage>.Instance
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
    public async Task should_store_published_message()
    {
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message, cancellationToken: AbortToken);
        mdMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task should_store_received_message()
    {
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);
        mdMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task should_store_received_exception_message()
    {
        var msgId = Guid.NewGuid().ToString("D");
        var content = "{\"Headers\":{\"headless-msg-id\":\"" + msgId + "\"},\"Value\":null}";
        await _storage.StoreReceivedExceptionMessageAsync(
            "test.name",
            "test.group",
            content,
            cancellationToken: AbortToken
        );
    }

    [Fact]
    public async Task should_change_publish_state()
    {
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message, cancellationToken: AbortToken);

        await _storage.ChangePublishStateAsync(mdMessage, StatusName.Succeeded, cancellationToken: AbortToken);
    }

    [Fact]
    public async Task should_change_receive_state()
    {
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message, cancellationToken: AbortToken);

        await _storage.ChangeReceiveStateAsync(mdMessage, StatusName.Succeeded, cancellationToken: AbortToken);
    }
}
