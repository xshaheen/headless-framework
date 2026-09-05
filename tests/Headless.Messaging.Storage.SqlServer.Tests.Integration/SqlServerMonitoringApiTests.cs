// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Transactions;
using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerMonitoringApiTests(SqlServerTestFixture fixture) : TestBase
{
    private SqlServerDataStorage _storage = null!;
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
            x.ConnectionString = fixture.ConnectionString;
            x.Schema = "messaging";
        });
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.UseStorageLock = true;
        });
        services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync();

        // Other classes in this collection share the `messaging` schema and not all reset on teardown,
        // so start each monitoring test from an empty table to keep the counts exact.
        await using (var resetConnection = new SqlConnection(fixture.ConnectionString))
        {
            await resetConnection.OpenAsync();
            await resetConnection.ExecuteAsync(
                "TRUNCATE TABLE messaging.published; TRUNCATE TABLE messaging.received;"
            );
        }

        _storage = new SqlServerDataStorage(
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            provider.GetRequiredService<IOptions<SqlServerOptions>>(),
            initializer,
            provider.GetRequiredService<ISerializer>(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            _timeProvider,
            new NullNodeMembership(),
            NullLogger<SqlServerDataStorage>.Instance
        );
        _monitoringApi = _storage.GetMonitoringApi();

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("TRUNCATE TABLE messaging.published; TRUNCATE TABLE messaging.received;");
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
        var count = await _monitoringApi.GetPublishedFailedCountAsync(AbortToken);

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
        var count = await _monitoringApi.GetPublishedSucceededCountAsync(AbortToken);

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
        var count = await _monitoringApi.GetReceivedFailedCountAsync(AbortToken);

        // then
        count.Should().Be(3);
    }

    [Fact]
    public async Task should_return_received_succeeded_count()
    {
        // given
        await _CreateReceivedMessage(StatusName.Succeeded);

        // when
        var count = await _monitoringApi.GetReceivedSucceededCountAsync(AbortToken);

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
        var id = stored.StorageId;

        // when
        var retrieved = await _monitoringApi.GetPublishedMessageAsync(id, AbortToken);

        // then
        retrieved.Should().NotBeNull();
        retrieved!.StorageId.Should().Be(stored.StorageId);
    }

    [Fact]
    public async Task should_return_null_for_nonexistent_published_message()
    {
        // when
        var retrieved = await _monitoringApi.GetPublishedMessageAsync(Guid.NewGuid(), AbortToken);

        // then
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task should_get_received_message_by_id()
    {
        // given
        var stored = await _CreateReceivedMessage(StatusName.Scheduled);
        var id = stored.StorageId;

        // when
        var retrieved = await _monitoringApi.GetReceivedMessageAsync(id, AbortToken);

        // then
        retrieved.Should().NotBeNull();
        retrieved!.StorageId.Should().Be(stored.StorageId);
    }

    #endregion

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task should_read_message_details_under_serializable_isolation(bool received, bool batch)
    {
        var stored = received
            ? await _CreateReceivedMessage(StatusName.Scheduled)
            : await _CreatePublishedMessage(StatusName.Scheduled);
        using var transaction = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
            TransactionScopeAsyncFlowOption.Enabled
        );

        if (batch)
        {
            var messages = received
                ? await _monitoringApi.GetReceivedMessagesAsync([stored.StorageId], AbortToken)
                : await _monitoringApi.GetPublishedMessagesAsync([stored.StorageId], AbortToken);
            messages.Should().ContainSingle().Which.StorageId.Should().Be(stored.StorageId);
        }
        else
        {
            var message = received
                ? await _monitoringApi.GetReceivedMessageAsync(stored.StorageId, AbortToken)
                : await _monitoringApi.GetPublishedMessageAsync(stored.StorageId, AbortToken);
            message.Should().NotBeNull();
            message!.StorageId.Should().Be(stored.StorageId);
        }

        transaction.Complete();
    }

    #region Get Messages By Ids Tests

    [Fact]
    public async Task should_get_published_messages_by_ids()
    {
        // given
        var first = await _CreatePublishedMessage(StatusName.Succeeded);
        var second = await _CreatePublishedMessage(StatusName.Succeeded);
        await _CreatePublishedMessage(StatusName.Succeeded); // stored but not requested

        // when - multiple ids resolved through the IN-list query
        var result = await _monitoringApi.GetPublishedMessagesAsync([first.StorageId, second.StorageId], AbortToken);

        // then
        result.Select(m => m.StorageId).Should().BeEquivalentTo([first.StorageId, second.StorageId]);
    }

    [Fact]
    public async Task should_get_received_messages_by_ids()
    {
        // given
        var first = await _CreateReceivedMessage(StatusName.Failed);
        var second = await _CreateReceivedMessage(StatusName.Succeeded);

        // when
        var result = await _monitoringApi.GetReceivedMessagesAsync([first.StorageId, second.StorageId], AbortToken);

        // then
        result.Select(m => m.StorageId).Should().BeEquivalentTo([first.StorageId, second.StorageId]);
    }

    [Fact]
    public async Task should_return_only_existing_messages_when_some_ids_missing()
    {
        // given
        var existing = await _CreatePublishedMessage(StatusName.Succeeded);

        // when - mix a stored id with one that was never persisted
        var result = await _monitoringApi.GetPublishedMessagesAsync([existing.StorageId, Guid.NewGuid()], AbortToken);

        // then
        result.Should().ContainSingle().Which.StorageId.Should().Be(existing.StorageId);
    }

    [Fact]
    public async Task should_return_empty_when_ids_empty()
    {
        // when
        var result = await _monitoringApi.GetPublishedMessagesAsync([], AbortToken);

        // then
        result.Should().BeEmpty();
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
            StatusName = StatusName.Succeeded,
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
    public async Task should_preserve_total_items_for_empty_later_pages()
    {
        // given
        for (var i = 0; i < 5; i++)
        {
            await _CreatePublishedMessage(StatusName.Succeeded);
        }

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            StatusName = StatusName.Succeeded,
            CurrentPage = 3,
            PageSize = 2,
        };

        // when
        var page = await _monitoringApi.GetMessagesAsync(query, AbortToken);

        // then
        page.Items.Should().BeEmpty();
        page.TotalItems.Should().Be(5);
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
            StatusName = StatusName.Failed,
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _monitoringApi.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(m => m.StatusName.Should().Be(StatusName.Failed));
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
        result.Items.Should().ContainSingle();
        result.Items.Single().Name.Should().Be("orders.created");
    }

    [Fact]
    public async Task should_project_delivery_metadata_without_failing_on_malformed_envelopes()
    {
        var explicitHeaders = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString("D"),
            [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Auto),
            [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.Durable),
        };
        var explicitPublished = await _storage.StoreMessageAsync(
            "delivery-metadata-explicit",
            new Message(explicitHeaders, null),
            cancellationToken: AbortToken
        );
        var malformedPublished = await _storage.StoreMessageAsync(
            "delivery-metadata-malformed",
            new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString("D"),
                },
                null
            ),
            cancellationToken: AbortToken
        );
        var legacyReceived = await _storage.StoreReceivedMessageAsync(
            "delivery-metadata-legacy",
            "delivery-metadata-group",
            new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString("D"),
                },
                null
            ),
            AbortToken
        );

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE messaging.published SET Content = @Content WHERE Id = @Id",
                new { Content = "not-a-message-envelope", Id = malformedPublished.StorageId }
            );
        }

        var publishedPage = await _monitoringApi.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = MessageType.Publish,
                CurrentPage = 0,
                PageSize = 10,
            },
            AbortToken
        );
        var receivedPage = await _monitoringApi.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = MessageType.Subscribe,
                CurrentPage = 0,
                PageSize = 10,
            },
            AbortToken
        );

        var explicitView = publishedPage.Items.Single(x => x.StorageId == explicitPublished.StorageId);
        explicitView.RequestedDeliveryMode.Should().Be(DeliveryMode.Auto);
        explicitView.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);

        var malformedView = publishedPage.Items.Single(x => x.StorageId == malformedPublished.StorageId);
        malformedView.RequestedDeliveryMode.Should().BeNull();
        malformedView.ResolvedDeliveryMode.Should().BeNull();

        var legacyView = receivedPage.Items.Single(x => x.StorageId == legacyReceived.StorageId);
        legacyView.RequestedDeliveryMode.Should().BeNull();
        legacyView.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
    }

    [Theory]
    [InlineData(MessageType.Publish, "Published")]
    [InlineData(MessageType.Subscribe, "Received")]
    public async Task should_return_bounded_deterministic_unknown_lane_diagnostics_without_mutation(
        MessageType messageType,
        string tableName
    )
    {
        var oldestId = Guid.NewGuid();
        var recognizedId = Guid.NewGuid();
        var newestId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await _InsertDiagnosticRowAsync(connection, tableName, oldestId, rawLane: 70, now.AddMinutes(-3));
        await _InsertDiagnosticRowAsync(connection, tableName, recognizedId, rawLane: 0, now.AddMinutes(-2));
        await _InsertDiagnosticRowAsync(connection, tableName, newestId, rawLane: 71, now.AddMinutes(-1));
        var before = (
            await connection.QueryAsync<(Guid Id, short RawLane, string Name, string StatusName, string Content)>(
                $"SELECT Id, IntentType, Name, StatusName, Content FROM messaging.{tableName} ORDER BY Added, Id"
            )
        ).ToList();

        var firstPage = await _monitoringApi.GetUnknownLaneMessagesAsync(
            new UnknownLaneMessageQuery
            {
                MessageType = messageType,
                CurrentPage = 1,
                PageSize = 1,
            },
            AbortToken
        );
        var secondPage = await _monitoringApi.GetUnknownLaneMessagesAsync(
            new UnknownLaneMessageQuery
            {
                MessageType = messageType,
                CurrentPage = 2,
                PageSize = 1,
            },
            AbortToken
        );
        var cappedPage = await _monitoringApi.GetUnknownLaneMessagesAsync(
            new UnknownLaneMessageQuery
            {
                MessageType = messageType,
                CurrentPage = 0,
                PageSize = 500,
            },
            AbortToken
        );

        firstPage.Items.Should().ContainSingle().Which.StorageId.Should().Be(oldestId);
        firstPage.Items[0].RawLane.Should().Be(70);
        firstPage.Items[0].MessageType.Should().Be(messageType);
        firstPage.TotalItems.Should().Be(2);
        secondPage.Items.Should().ContainSingle().Which.StorageId.Should().Be(newestId);
        cappedPage.Index.Should().Be(0);
        cappedPage.Size.Should().Be(200);
        cappedPage.Items.Select(message => message.StorageId).Should().Equal(oldestId, newestId);
        typeof(UnknownLaneMessageView).GetProperties().Select(property => property.Name).Should().NotContain("Content");

        var after = (
            await connection.QueryAsync<(Guid Id, short RawLane, string Name, string StatusName, string Content)>(
                $"SELECT Id, IntentType, Name, StatusName, Content FROM messaging.{tableName} ORDER BY Added, Id"
            )
        ).ToList();
        after.Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(MessageType.Publish, "Published")]
    [InlineData(MessageType.Subscribe, "Received")]
    public async Task should_hide_malformed_unknown_lane_from_ordinary_monitoring_reads(
        MessageType messageType,
        string tableName
    )
    {
        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await _InsertDiagnosticRowAsync(connection, tableName, id, rawLane: 70, added: _timeProvider.GetUtcNow());

        var single =
            messageType == MessageType.Publish
                ? await _monitoringApi.GetPublishedMessageAsync(id, AbortToken)
                : await _monitoringApi.GetReceivedMessageAsync(id, AbortToken);
        var multiple =
            messageType == MessageType.Publish
                ? await _monitoringApi.GetPublishedMessagesAsync([id], AbortToken)
                : await _monitoringApi.GetReceivedMessagesAsync([id], AbortToken);
        var count =
            messageType == MessageType.Publish
                ? await _monitoringApi.GetPublishedFailedCountAsync(AbortToken)
                : await _monitoringApi.GetReceivedFailedCountAsync(AbortToken);
        var page = await _monitoringApi.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = messageType,
                CurrentPage = 0,
                PageSize = 10,
            },
            AbortToken
        );

        single.Should().BeNull();
        multiple.Should().BeEmpty();
        count.Should().Be(0);
        page.Items.Should().BeEmpty();
        (
            await _monitoringApi.GetUnknownLaneMessagesAsync(
                new UnknownLaneMessageQuery
                {
                    MessageType = messageType,
                    CurrentPage = 1,
                    PageSize = 10,
                },
                AbortToken
            )
        ).Items.Should().ContainSingle().Which.StorageId.Should().Be(id);
    }

    [Fact]
    public async Task should_reject_unknown_message_type_for_unknown_lane_diagnostics()
    {
        var act = async () =>
            await _monitoringApi.GetUnknownLaneMessagesAsync(
                new UnknownLaneMessageQuery { MessageType = (MessageType)42 },
                AbortToken
            );

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Hourly Timeline Tests

    [Fact]
    public async Task should_return_hourly_failed_jobs_timeline()
    {
        // given
        await _CreatePublishedMessage(StatusName.Failed);

        // when
        var timeline = await _monitoringApi.GetHourlyFailedJobsAsync(MessageType.Publish, AbortToken);

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
        var timeline = await _monitoringApi.GetHourlySucceededJobsAsync(MessageType.Subscribe, AbortToken);

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
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var stored = await _storage.StoreMessageAsync(name, message, null, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().AddHours(1);
        await _storage.ChangePublishStateAsync(stored, status, cancellationToken: AbortToken);

        return stored;
    }

    private async Task<MediumMessage> _CreateReceivedMessage(StatusName status)
    {
        var msgId = Guid.NewGuid().ToString("D");
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        var stored = await _storage.StoreReceivedMessageAsync("test.name", "test.group", message, AbortToken);
        stored.ExpiresAt = _timeProvider.GetUtcNow().AddHours(1);
        await _storage.ChangeReceiveStateAsync(stored, status, cancellationToken: AbortToken);

        return stored;
    }

    private static Task _InsertDiagnosticRowAsync(
        SqlConnection connection,
        string tableName,
        Guid id,
        short rawLane,
        DateTimeOffset added
    )
    {
        var receivedColumns = string.Equals(tableName, "Received", StringComparison.Ordinal)
            ? ", [Group], ExceptionInfo"
            : string.Empty;
        var receivedValues = string.Equals(tableName, "Received", StringComparison.Ordinal)
            ? ", 'diagnostic-group', NULL"
            : string.Empty;
        return connection.ExecuteAsync(
            $"""
            INSERT INTO messaging.{tableName}
                (Id, Version, Name, Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId{receivedColumns})
            VALUES
                (@Id, 'v1', @Name, @Content, @IntentType, 0, @Added, NULL, @NextRetryAt, NULL, NULL, 'Failed', @MessageId{receivedValues});
            """,
            new
            {
                Id = id,
                Name = $"diagnostic-{rawLane}",
                Content = $"opaque-{rawLane}",
                IntentType = rawLane,
                Added = added,
                NextRetryAt = added,
                MessageId = $"diagnostic-{id:N}",
            }
        );
    }
}
