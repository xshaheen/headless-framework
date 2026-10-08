// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Persistence;
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
    private RelationalDataStorage _storage = null!;
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
            x.Version = "v1"; // Must match MessagingOptions.Version for retry queries
        });
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddTestMessagingSchema();
        services.AddSingleton<IMessageSerializer, JsonUtf8Serializer>();

        var provider = services.BuildServiceProvider();
        var tableNames = provider.GetRequiredService<IStorageTableNames>();
        await provider.ApplyMessagingSchemaAsync();
        _storage = new RelationalDataStorage(
            provider.GetRequiredService<IOptions<SqlServerOptions>>().Value.ToStorage(),
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            TestStorageOptions.For(),
            tableNames,
            provider.GetRequiredService<IMessageSerializer>(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            _timeProvider,
            new NullNodeMembership(),
            NullLogger<RelationalDataStorage>.Instance
        );

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "TRUNCATE TABLE headless.MessagingPublished; TRUNCATE TABLE headless.MessagingReceived;"
        );
        await base.DisposeAsyncCore();
    }

    #region Message CRUD Tests

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
    public async Task should_return_zero_when_deleting_nonexistent_published_message()
    {
        // when
        var deleted = await _storage.DeletePublishedMessageAsync(Guid.NewGuid(), AbortToken);

        // then
        deleted.Should().Be(0);
    }

    #endregion

    #region State Transition Tests

    [Fact]
    public async Task should_handle_empty_array_for_delayed_state_change()
    {
        // when & then - should not throw
        await _storage.ChangePublishStateToDelayedAsync([], AbortToken);
    }

    #endregion
}
