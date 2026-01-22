using Dapper;
using Framework.Abstractions;
using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerStorageConnectionTest(SqlServerTestFixture fixture) : TestBase
{
    private SqlServerDataStorage _storage = null!;
    private ILongIdGenerator _longIdGenerator = null!;

    public override async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<SqlServerOptions>(x => x.ConnectionString = fixture.Container.GetConnectionString());
        services.Configure<MessagingOptions>(x => x.Version = "v1");
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
            TimeProvider.System
        );

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("TRUNCATE TABLE messaging.published; TRUNCATE TABLE messaging.received;");
        await base.DisposeAsyncCore();
    }

    [Fact]
    public void should_store_published_message()
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = _storage.StoreMessageAsync("test.name", message, null, AbortToken);
        mdMessage.Should().NotBeNull();
    }

    [Fact]
    public void should_store_received_message()
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);
        mdMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task should_store_received_exception_message()
    {
        await _storage.StoreReceivedExceptionMessageAsync("test.name", "test.group", "", AbortToken);
    }

    [Fact]
    public async Task should_change_publish_state()
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        await _storage.ChangePublishStateAsync(mdMessage, StatusName.Succeeded, null, AbortToken);
    }

    [Fact]
    public async Task should_change_receive_state()
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message, null, AbortToken);

        await _storage.ChangeReceiveStateAsync(mdMessage, StatusName.Succeeded, AbortToken);
    }
}
