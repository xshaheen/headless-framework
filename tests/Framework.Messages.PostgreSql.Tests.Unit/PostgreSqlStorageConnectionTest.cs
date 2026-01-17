using Framework.Abstractions;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Persistence;
using Framework.Messages.Serialization;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection("PostgreSql")]
public class PostgreSqlStorageConnectionTest : DatabaseTestHost
{
    private readonly PostgreSqlDataStorage _storage;
    private readonly ILongIdGenerator _longIdGenerator;

    public PostgreSqlStorageConnectionTest()
    {
        var serializer = GetService<ISerializer>();
        var options = GetService<IOptions<PostgreSqlOptions>>();
        var capOptions = GetService<IOptions<CapOptions>>();
        var initializer = GetService<IStorageInitializer>();
        _longIdGenerator = GetService<ILongIdGenerator>();
        _storage = new PostgreSqlDataStorage(options, capOptions, initializer, serializer, _longIdGenerator);
    }

    [Fact]
    public void StorageMessageTest()
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = _storage.StoreMessageAsync("test.name", message);
        mdMessage.Should().NotBeNull();
    }

    [Fact]
    public void StoreReceivedMessageTest()
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = _storage.StoreReceivedMessageAsync("test.name", "test.group", message);
        mdMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task StoreReceivedExceptionMessageTest()
    {
        await _storage.StoreReceivedExceptionMessageAsync("test.name", "test.group", "");
    }

    [Fact]
    public async Task ChangePublishStateTest()
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message);

        await _storage.ChangePublishStateAsync(mdMessage, StatusName.Succeeded);
    }

    [Fact]
    public async Task ChangeReceiveStateTest()
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var mdMessage = await _storage.StoreMessageAsync("test.name", message);

        await _storage.ChangeReceiveStateAsync(mdMessage, StatusName.Succeeded);
    }
}
