// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Framework.Abstractions;
using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerMonitoringApiTests(SqlServerTestFixture fixture) : TestBase
{
    private SqlServerDataStorage _storage = null!;
    private ILongIdGenerator _longIdGenerator = null!;
    private FakeTimeProvider _timeProvider = null!;
    private IMonitoringApi _monitoringApi = null!;

    public override async ValueTask InitializeAsync()
    {
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<SqlServerOptions>(x =>
        {
            x.ConnectionString = fixture.Container.GetConnectionString();
            x.Schema = "messaging";
        });
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.UseStorageLock = true;
        });
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
            _timeProvider
        );
        _monitoringApi = _storage.GetMonitoringApi();

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "TRUNCATE TABLE messaging.published; TRUNCATE TABLE messaging.received; DELETE FROM messaging.Lock;"
        );
        await base.DisposeAsyncCore();
    }

    #region Statistics Tests

    [Fact]
    public async Task should_return_statistics_with_counts_by_status()
    {
        // given - create messages with different statuses
        await _CreatePublishedMessage(StatusName.Succeeded);
        await _CreatePublishedMessage(StatusName.Succeeded);
        await _CreatePublishedMessage(StatusName.Failed);
        await _CreateReceivedMessage(StatusName.Succeeded);
        await _CreateReceivedMessage(StatusName.Failed);
        await _CreateReceivedMessage(StatusName.Failed);

        // when
        var stats = await _monitoringApi.GetStatisticsAsync(AbortToken);

        // then
        stats.PublishedSucceeded.Should().Be(2);
        stats.PublishedFailed.Should().Be(1);
        stats.ReceivedSucceeded.Should().Be(1);
        stats.ReceivedFailed.Should().Be(2);
    }

    [Fact]
    public async Task should_return_zero_counts_when_no_messages()
    {
        // when
        var stats = await _monitoringApi.GetStatisticsAsync(AbortToken);

        // then
        stats.PublishedSucceeded.Should().Be(0);
        stats.PublishedFailed.Should().Be(0);
        stats.ReceivedSucceeded.Should().Be(0);
        stats.ReceivedFailed.Should().Be(0);
        stats.PublishedDelayed.Should().Be(0);
    }

    #endregion

    #region Count Tests

    [Fact]
    public async Task should_return_published_failed_count()
    {
        // given
        await _CreatePublishedMessage(StatusName.Failed);
        await _CreatePublishedMessage(StatusName.Failed);
        await _CreatePublishedMessage(StatusName.Succeeded);

        // when
        var count = await _monitoringApi.PublishedFailedCount(AbortToken);

        // then
        count.Should().Be(2);
    }

    [Fact]
    public async Task should_return_published_succeeded_count()
    {
        // given
        await _CreatePublishedMessage(StatusName.Succeeded);
        await _CreatePublishedMessage(StatusName.Failed);

        // when
        var count = await _monitoringApi.PublishedSucceededCount(AbortToken);

        // then
        count.Should().Be(1);
    }

    [Fact]
    public async Task should_return_received_failed_count()
    {
        // given
        await _CreateReceivedMessage(StatusName.Failed);
        await _CreateReceivedMessage(StatusName.Failed);
        await _CreateReceivedMessage(StatusName.Failed);

        // when
        var count = await _monitoringApi.ReceivedFailedCount(AbortToken);

        // then
        count.Should().Be(3);
    }

    [Fact]
    public async Task should_return_received_succeeded_count()
    {
        // given
        await _CreateReceivedMessage(StatusName.Succeeded);

        // when
        var count = await _monitoringApi.ReceivedSucceededCount(AbortToken);

        // then
        count.Should().Be(1);
    }

    #endregion

    #region Get Message By Id Tests

    [Fact]
    public async Task should_get_published_message_by_id()
    {
        // given
        var stored = await _CreatePublishedMessage(StatusName.Scheduled);
        var id = long.Parse(stored.DbId, CultureInfo.InvariantCulture);

        // when
        var retrieved = await _monitoringApi.GetPublishedMessageAsync(id, AbortToken);

        // then
        retrieved.Should().NotBeNull();
        retrieved!.DbId.Should().Be(stored.DbId);
    }

    [Fact]
    public async Task should_return_null_for_nonexistent_published_message()
    {
        // when
        var retrieved = await _monitoringApi.GetPublishedMessageAsync(999999999L, AbortToken);

        // then
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task should_get_received_message_by_id()
    {
        // given
        var stored = await _CreateReceivedMessage(StatusName.Scheduled);
        var id = long.Parse(stored.DbId, CultureInfo.InvariantCulture);

        // when
        var retrieved = await _monitoringApi.GetReceivedMessageAsync(id, AbortToken);

        // then
        retrieved.Should().NotBeNull();
        retrieved!.DbId.Should().Be(stored.DbId);
    }

    #endregion

    #region Pagination Tests

    [Fact]
    public async Task should_support_pagination_for_published_messages()
    {
        // given - create 5 messages
        for (var i = 0; i < 5; i++)
        {
            await _CreatePublishedMessage(StatusName.Succeeded);
        }

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            StatusName = nameof(StatusName.Succeeded),
            CurrentPage = 0,
            PageSize = 2,
        };

        // when
        var page1 = await _monitoringApi.GetMessagesAsync(query, AbortToken);

        // then
        page1.Items.Should().HaveCount(2);
        page1.TotalItems.Should().Be(5);
        page1.Index.Should().Be(0);
        page1.Size.Should().Be(2);
    }

    [Fact]
    public async Task should_filter_messages_by_status()
    {
        // given
        await _CreatePublishedMessage(StatusName.Succeeded);
        await _CreatePublishedMessage(StatusName.Failed);
        await _CreatePublishedMessage(StatusName.Failed);

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            StatusName = nameof(StatusName.Failed),
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _monitoringApi.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(m => m.StatusName.Should().Be(nameof(StatusName.Failed)));
    }

    [Fact]
    public async Task should_filter_messages_by_name()
    {
        // given
        await _CreatePublishedMessageWithName("orders.created", StatusName.Succeeded);
        await _CreatePublishedMessageWithName("users.updated", StatusName.Succeeded);

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            Name = "orders.created",
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _monitoringApi.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().HaveCount(1);
        result.Items.Single().Name.Should().Be("orders.created");
    }

    #endregion

    #region Hourly Timeline Tests

    [Fact]
    public async Task should_return_hourly_failed_jobs_timeline()
    {
        // given
        await _CreatePublishedMessage(StatusName.Failed);

        // when
        var timeline = await _monitoringApi.HourlyFailedJobs(MessageType.Publish, AbortToken);

        // then
        timeline.Should().NotBeEmpty();
        timeline.Should().HaveCount(24); // 24 hours
    }

    [Fact]
    public async Task should_return_hourly_succeeded_jobs_timeline()
    {
        // given
        await _CreateReceivedMessage(StatusName.Succeeded);

        // when
        var timeline = await _monitoringApi.HourlySucceededJobs(MessageType.Subscribe, AbortToken);

        // then
        timeline.Should().NotBeEmpty();
        timeline.Should().HaveCount(24);
    }

    #endregion

    private async Task<MediumMessage> _CreatePublishedMessage(StatusName status)
    {
        return await _CreatePublishedMessageWithName("test.name", status);
    }

    private async Task<MediumMessage> _CreatePublishedMessageWithName(string name, StatusName status)
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var stored = await _storage.StoreMessageAsync(name, message, null, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
        await _storage.ChangePublishStateAsync(stored, status, null, AbortToken);

        return stored;
    }

    private async Task<MediumMessage> _CreateReceivedMessage(StatusName status)
    {
        var msgId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var stored = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
        await _storage.ChangeReceiveStateAsync(stored, status, AbortToken);

        return stored;
    }
}
