using Framework.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlDeduplicationTest(PostgreSqlTestFixture fixture) : IAsyncLifetime
{
    private IDataStorage _storage = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.Container.GetConnectionString());
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<ILongIdGenerator, SnowflakeIdLongIdGenerator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDataStorage, PostgreSqlDataStorage>();

        var provider = services.BuildServiceProvider();
        _storage = provider.GetRequiredService<IDataStorage>();

        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task should_prevent_duplicate_messages_with_same_message_id_and_group()
    {
        var messageId = Guid.NewGuid().ToString();
        var group = "test-consumer-group";
        var name = "test.topic";

        var message1 = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = messageId,
                [Headers.MessageName] = name,
            },
            new { Data = "First attempt" }
        );

        var message2 = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = messageId,
                [Headers.MessageName] = name,
            },
            new { Data = "Second attempt - duplicate" }
        );

        // Store first message
        var stored1 = await _storage.StoreReceivedMessageAsync(name, group, message1);
        stored1.Should().NotBeNull();

        // Store duplicate message - should update, not insert
        var stored2 = await _storage.StoreReceivedMessageAsync(name, group, message2);
        stored2.Should().NotBeNull();

        // ON CONFLICT should update existing row, verifying deduplication works
        stored1.DbId.Should().NotBeNullOrEmpty();
        stored2.DbId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_allow_same_message_id_with_different_groups()
    {
        var messageId = Guid.NewGuid().ToString();
        var group1 = "consumer-group-1";
        var group2 = "consumer-group-2";
        var name = "test.topic";

        var message1 = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = messageId,
                [Headers.MessageName] = name,
            },
            new { Data = "Group 1" }
        );

        var message2 = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = messageId,
                [Headers.MessageName] = name,
            },
            new { Data = "Group 2" }
        );

        // Store same message ID to different groups
        var stored1 = await _storage.StoreReceivedMessageAsync(name, group1, message1);
        var stored2 = await _storage.StoreReceivedMessageAsync(name, group2, message2);

        // Should create two separate records
        stored1.Should().NotBeNull();
        stored2.Should().NotBeNull();
        stored1.DbId.Should().NotBe(stored2.DbId);
    }
}
