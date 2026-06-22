using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerStorageConnectionTest(SqlServerTestFixture fixture) : TestBase
{
    private SqlServerDataStorage _storage = null!;

    public override async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<SqlServerOptions>(x =>
        {
            x.ConnectionString = fixture.ConnectionString;
            x.Version = "v1"; // Must match MessagingOptions.Version
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
            TimeProvider.System,
            new NullNodeMembership(),
            NullLogger<SqlServerDataStorage>.Instance
        );

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("TRUNCATE TABLE messaging.Published; TRUNCATE TABLE messaging.Received;");
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_store_published_message()
    {
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);
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

        var mdMessage = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        await _storage.ChangePublishStateAsync(mdMessage, StatusName.Succeeded, cancellationToken: AbortToken);
    }

    [Fact]
    public async Task should_change_receive_state()
    {
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        await _storage.ChangeReceiveStateAsync(mdMessage, StatusName.Succeeded, cancellationToken: AbortToken);
    }
}
