// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

/// <summary>
/// Integration tests for SQL Server data storage using real SQL Server container.
/// Inherits from <see cref="DataStorageTestsBase"/> to run standard storage tests.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerStorageTests(SqlServerTestFixture fixture) : DataStorageTestsBase
{
    private IStorageInitializer? _initializer;
    private IDataStorage? _storage;
    private ISerializer? _serializer;

    /// <inheritdoc />
    protected override DataStorageCapabilities Capabilities =>
        new()
        {
            SupportsExpiration = true,
            SupportsConcurrentOperations = true,
            SupportsDelayedScheduling = true,
            SupportsMonitoringApi = true,
        };

    /// <inheritdoc />
    protected override IDataStorage GetStorage()
    {
        _EnsureInitialized();
        return _storage!;
    }

    /// <inheritdoc />
    protected override IStorageInitializer GetInitializer()
    {
        _EnsureInitialized();
        return _initializer!;
    }

    /// <inheritdoc />
    protected override ISerializer GetSerializer()
    {
        _EnsureInitialized();
        return _serializer!;
    }

    /// <inheritdoc />
    protected override async Task<int> CountReceivedMessagesByIdentityAsync(
        string messageId,
        string? group,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sqlWithGroup =
            "SELECT COUNT(*) FROM messaging.Received WHERE [MessageId] = @MessageId AND [Group] = @Group";
        const string sqlWithoutGroup =
            "SELECT COUNT(*) FROM messaging.Received WHERE [MessageId] = @MessageId AND [Group] IS NULL";

        return group is null
            ? await connection.ExecuteScalarAsync<int>(sqlWithoutGroup, new { MessageId = messageId })
            : await connection.ExecuteScalarAsync<int>(sqlWithGroup, new { MessageId = messageId, Group = group });
    }

    /// <inheritdoc />
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _EnsureInitialized();
        await _initializer!.InitializeAsync(AbortToken);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        // Clean up tables after tests
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("TRUNCATE TABLE messaging.Published; TRUNCATE TABLE messaging.Received;");

        await base.DisposeAsyncCore();
    }

    private void _EnsureInitialized()
    {
        if (_initializer is not null)
        {
            return;
        }

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
            x.RetryPolicy.MaxPersistedRetries = 4;
            x.FailedMessageExpiredAfter = 3600;
            x.UseStorageLock = true;
        });
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton(TimeProvider.System);

        var provider = services.BuildServiceProvider();

        var sqlServerOptions = provider.GetRequiredService<IOptions<SqlServerOptions>>();
        var messagingOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();
        _serializer = provider.GetRequiredService<ISerializer>();

        _initializer = new SqlServerStorageInitializer(
            NullLogger<SqlServerStorageInitializer>.Instance,
            sqlServerOptions,
            messagingOptions
        );

        _storage = new SqlServerDataStorage(
            messagingOptions,
            sqlServerOptions,
            _initializer,
            provider.GetRequiredService<ISerializer>(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            NodeMembership,
            NullLogger<SqlServerDataStorage>.Instance
        );
    }

    #region Data Storage Tests

    [Fact]
    public override Task should_initialize_schema() => base.should_initialize_schema();

    [Fact]
    public override Task should_get_table_names() => base.should_get_table_names();

    [Fact]
    public override Task should_store_published_message() => base.should_store_published_message();

    [Fact]
    public override Task should_store_published_message_with_non_numeric_message_id() =>
        base.should_store_published_message_with_non_numeric_message_id();

    [Fact]
    public override Task should_store_received_message() => base.should_store_received_message();

    [Fact]
    public override Task should_store_received_exception_message() => base.should_store_received_exception_message();

    [Fact]
    public override Task should_change_publish_state() => base.should_change_publish_state();

    [Fact]
    public override Task should_change_receive_state() => base.should_change_receive_state();

    [Fact]
    public override Task should_change_publish_state_to_delayed() => base.should_change_publish_state_to_delayed();

    [Fact]
    public override Task should_not_flip_terminal_published_row_back_to_delayed() =>
        base.should_not_flip_terminal_published_row_back_to_delayed();

    [Fact]
    public override Task should_ignore_unknown_storage_ids_when_flushing_delayed_state() =>
        base.should_ignore_unknown_storage_ids_when_flushing_delayed_state();

    [Fact]
    public override Task should_get_published_messages_of_need_retry() =>
        base.should_get_published_messages_of_need_retry();

    [Fact]
    public override Task should_get_received_messages_of_need_retry() =>
        base.should_get_received_messages_of_need_retry();

    [Fact]
    public override Task should_delete_expired_messages() => base.should_delete_expired_messages();

    [Fact]
    public override Task should_not_delete_expired_failed_messages_with_pending_retry() =>
        base.should_not_delete_expired_failed_messages_with_pending_retry();

    [Fact]
    public override Task should_delete_published_message() => base.should_delete_published_message();

    [Fact]
    public override Task should_delete_received_message() => base.should_delete_received_message();

    [Fact]
    public override Task should_get_monitoring_api() => base.should_get_monitoring_api();

    [Fact]
    public override Task should_handle_concurrent_storage_operations() =>
        base.should_handle_concurrent_storage_operations();

    [Fact]
    public override Task should_schedule_messages_of_delayed() => base.should_schedule_messages_of_delayed();

    [Fact]
    public override Task should_store_message_with_transaction() => base.should_store_message_with_transaction();

    [Fact]
    public override Task should_handle_message_state_transitions() => base.should_handle_message_state_transitions();

    [Fact]
    public override Task should_handle_failed_message_state() => base.should_handle_failed_message_state();

    [Fact]
    public override Task should_not_return_published_message_with_failed_status_and_null_next_retry_at() =>
        base.should_not_return_published_message_with_failed_status_and_null_next_retry_at();

    [Fact]
    public override Task should_seal_succeeded_published_message_against_state_change_and_retry_pickup() =>
        base.should_seal_succeeded_published_message_against_state_change_and_retry_pickup();

    [Fact]
    public override Task should_not_return_published_message_with_future_next_retry_at() =>
        base.should_not_return_published_message_with_future_next_retry_at();

    [Fact]
    public override Task should_not_return_received_message_with_failed_status_and_null_next_retry_at() =>
        base.should_not_return_received_message_with_failed_status_and_null_next_retry_at();

    [Fact]
    public override Task should_not_return_received_message_with_future_next_retry_at() =>
        base.should_not_return_received_message_with_future_next_retry_at();

    [Fact]
    public override Task should_not_return_leased_published_message_until_lease_expires() =>
        base.should_not_return_leased_published_message_until_lease_expires();

    [Fact]
    public override Task should_reject_mismatched_original_retries() =>
        base.should_reject_mismatched_original_retries();

    [Fact]
    public override Task should_report_false_when_received_exception_message_is_already_terminal() =>
        base.should_report_false_when_received_exception_message_is_already_terminal();

    [Fact]
    public override Task should_handle_concurrent_redelivery_storm_on_same_message_id() =>
        base.should_handle_concurrent_redelivery_storm_on_same_message_id();

    [Fact]
    public override Task should_handle_concurrent_first_insert_storm_with_null_and_non_null_group() =>
        base.should_handle_concurrent_first_insert_storm_with_null_and_non_null_group();

    [Fact]
    public override Task should_handle_concurrent_store_received_message_with_same_identity() =>
        base.should_handle_concurrent_store_received_message_with_same_identity();

    [Fact]
    public override Task should_pickup_message_at_max_persisted_retries_and_exclude_above() =>
        base.should_pickup_message_at_max_persisted_retries_and_exclude_above();

    [Fact]
    public override Task should_not_return_leased_received_message_until_lease_expires() =>
        base.should_not_return_leased_received_message_until_lease_expires();

    [Fact]
    public override Task should_return_unstored_snapshot_when_redelivery_hits_active_receive_lease() =>
        base.should_return_unstored_snapshot_when_redelivery_hits_active_receive_lease();

    [Fact]
    public override Task should_handle_concurrent_state_updates_to_same_row() =>
        base.should_handle_concurrent_state_updates_to_same_row();

    [Fact]
    public override Task should_reclaim_published_retry_row_owned_by_dead_node() =>
        base.should_reclaim_published_retry_row_owned_by_dead_node();

    [Fact]
    public override Task should_reclaim_received_retry_row_owned_by_dead_node() =>
        base.should_reclaim_received_retry_row_owned_by_dead_node();

    [Fact]
    public override Task should_stamp_owner_on_claim() => base.should_stamp_owner_on_claim();

    [Fact]
    public override Task should_not_reclaim_rows_of_live_or_restarted_incarnation() =>
        base.should_not_reclaim_rows_of_live_or_restarted_incarnation();

    [Fact]
    public override Task should_not_reclaim_terminal_rows() => base.should_not_reclaim_terminal_rows();

    [Fact]
    public override Task should_be_inert_when_no_dead_owners_passed() =>
        base.should_be_inert_when_no_dead_owners_passed();

    [Fact]
    public override Task should_not_reclaim_rows_with_null_owner() => base.should_not_reclaim_rows_with_null_owner();

    [Fact]
    public override Task should_reclaim_dead_owner_rows_idempotently() =>
        base.should_reclaim_dead_owner_rows_idempotently();

    [Fact]
    public override Task should_not_reclaim_dead_owner_rows_with_expired_lease() =>
        base.should_not_reclaim_dead_owner_rows_with_expired_lease();

    #endregion

    #region SQL Server-Specific Tests

    [Fact]
    public async Task should_create_database_schema()
    {
        // given, when
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // then
        var result = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'messaging'"
        );
        result.Should().Be("messaging");
    }

    [Theory]
    [InlineData("Published")]
    [InlineData("Received")]
    public async Task should_create_tables(string tableName)
    {
        // given, when
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // then
        var result = await connection.QueryFirstOrDefaultAsync<string>(
            $"""
            SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'messaging' AND TABLE_NAME = '{tableName}'
            """
        );
        result.Should().Be(tableName);
    }

    [Theory]
    [InlineData("Published")]
    [InlineData("Received")]
    public async Task should_create_owner_column_with_shared_width(string tableName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var dataType = await connection.QueryFirstOrDefaultAsync<string>(
            """
            SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'messaging' AND TABLE_NAME = @TableName AND COLUMN_NAME = 'Owner'
            """,
            new { TableName = tableName }
        );
        var maxLength = await connection.QueryFirstOrDefaultAsync<int?>(
            """
            SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'messaging' AND TABLE_NAME = @TableName AND COLUMN_NAME = 'Owner'
            """,
            new { TableName = tableName }
        );

        dataType.Should().Be("nvarchar");
        maxLength.Should().Be(DataStorageConstants.OwnerColumnMaxLength);
    }

    [Fact]
    public async Task should_return_sqlserver_monitoring_api()
    {
        // given
        var storage = GetStorage();

        // when
        var monitoringApi = storage.GetMonitoringApi();

        // then
        monitoringApi.Should().BeOfType<SqlServerMonitoringApi>();
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("Published")]
    [InlineData("Received")]
    public async Task should_terminalize_poison_retry_row_when_content_cannot_deserialize(string tableName)
    {
        // given
        var storage = GetStorage();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await _InsertPoisonRetryRowAsync(connection, tableName, id, now);
        }

        // when
        var picked = string.Equals(tableName, "Published", StringComparison.Ordinal)
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);

        // then
        picked.Should().NotContain(message => message.StorageId == id);

        await using var assertConnection = new SqlConnection(fixture.ConnectionString);
        await assertConnection.OpenAsync(AbortToken);

        var statusName = await assertConnection.ExecuteScalarAsync<string>(
            $"SELECT StatusName FROM messaging.{tableName} WHERE Id = @Id",
            new { Id = id }
        );
        var nextRetryAt = await assertConnection.ExecuteScalarAsync<DateTime?>(
            $"SELECT NextRetryAt FROM messaging.{tableName} WHERE Id = @Id",
            new { Id = id }
        );
        var lockedUntil = await assertConnection.ExecuteScalarAsync<DateTime?>(
            $"SELECT LockedUntil FROM messaging.{tableName} WHERE Id = @Id",
            new { Id = id }
        );
        var owner = await assertConnection.ExecuteScalarAsync<string?>(
            $"SELECT Owner FROM messaging.{tableName} WHERE Id = @Id",
            new { Id = id }
        );

        statusName.Should().Be(nameof(StatusName.Failed));
        nextRetryAt.Should().BeNull();
        lockedUntil.Should().BeNull();
        owner.Should().BeNull();

        if (string.Equals(tableName, "Received", StringComparison.Ordinal))
        {
            var exceptionInfo = await assertConnection.ExecuteScalarAsync<string?>(
                "SELECT ExceptionInfo FROM messaging.Received WHERE Id = @Id",
                new { Id = id }
            );
            exceptionInfo.Should().Contain("JsonException");
        }
    }

    [Theory]
    [InlineData("Published")]
    [InlineData("Received")]
    public async Task should_return_healthy_retry_row_when_same_claim_batch_contains_poison(string tableName)
    {
        var storage = GetStorage();
        var serializer = GetSerializer();
        var poisonId = Guid.NewGuid();
        var healthyId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await _InsertPoisonRetryRowAsync(connection, tableName, poisonId, now);
            await _InsertHealthyRetryRowAsync(
                connection,
                tableName,
                healthyId,
                serializer.Serialize(CreateMessage("healthy-retry")),
                now
            );
        }

        var picked = string.Equals(tableName, "Published", StringComparison.Ordinal)
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);

        picked.Select(message => message.StorageId).Should().Contain(healthyId).And.NotContain(poisonId);

        await using var assertConnection = new SqlConnection(fixture.ConnectionString);
        await assertConnection.OpenAsync(AbortToken);
        var poisonNextRetryAt = await assertConnection.ExecuteScalarAsync<DateTime?>(
            $"SELECT NextRetryAt FROM messaging.{tableName} WHERE Id = @Id",
            new { Id = poisonId }
        );
        poisonNextRetryAt.Should().BeNull();
    }

    [Fact]
    public async Task should_apply_scheduler_batch_size_across_delayed_and_queued_branches()
    {
        // given
        var storage = _CreateStorage(new MessagingOptions { Version = "v1", SchedulerBatchSize = 1 });
        var serializer = GetSerializer();
        var now = DateTime.UtcNow;
        var delayedId = Guid.NewGuid();
        var queuedId = Guid.NewGuid();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);

            await _InsertPublishedRowAsync(
                connection,
                delayedId,
                serializer.Serialize(CreateMessage("sql-delayed")),
                StatusName.Delayed,
                expiresAt: now,
                nextRetryAt: null
            );
            await _InsertPublishedRowAsync(
                connection,
                queuedId,
                serializer.Serialize(CreateMessage("sql-queued")),
                StatusName.Queued,
                expiresAt: now.AddMinutes(-2),
                nextRetryAt: null
            );
        }

        var scheduled = new List<MediumMessage>();

        // when
        await storage.ScheduleMessagesOfDelayedAsync(
            (_, messages) =>
            {
                scheduled.AddRange(messages);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );

        // then
        scheduled.Should().ContainSingle();
        new[] { delayedId, queuedId }.Should().Contain(scheduled[0].StorageId);
    }

    [Fact]
    public async Task should_not_lock_scheduler_candidates_beyond_batch_size()
    {
        // given
        var firstStorage = _CreateStorage(new MessagingOptions { Version = "v1", SchedulerBatchSize = 1 });
        var secondStorage = _CreateStorage(new MessagingOptions { Version = "v1", SchedulerBatchSize = 1 });
        var serializer = GetSerializer();
        var now = DateTime.UtcNow;

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);

            for (var index = 0; index < 3; index++)
            {
                await _InsertPublishedRowAsync(
                    connection,
                    Guid.NewGuid(),
                    serializer.Serialize(CreateMessage($"sql-delayed-{index}")),
                    StatusName.Delayed,
                    expiresAt: now.AddMinutes(-10 + index),
                    nextRetryAt: null
                );
                await _InsertPublishedRowAsync(
                    connection,
                    Guid.NewGuid(),
                    serializer.Serialize(CreateMessage($"sql-queued-{index}")),
                    StatusName.Queued,
                    expiresAt: now.AddMinutes(-20 + index),
                    nextRetryAt: null
                );
            }
        }

        var firstMessages = new List<MediumMessage>();
        var secondMessages = new List<MediumMessage>();
        var firstSchedulerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstScheduler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // when
        var firstSchedule = firstStorage
            .ScheduleMessagesOfDelayedAsync(
                async (_, messages) =>
                {
                    firstMessages.AddRange(messages);
                    firstSchedulerEntered.SetResult();
                    await releaseFirstScheduler.Task.WaitAsync(AbortToken);
                },
                AbortToken
            )
            .AsTask();

        await firstSchedulerEntered.Task.WaitAsync(AbortToken);

        await secondStorage.ScheduleMessagesOfDelayedAsync(
            (_, messages) =>
            {
                secondMessages.AddRange(messages);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );

        releaseFirstScheduler.SetResult();
        await firstSchedule.WaitAsync(AbortToken);

        // then
        firstMessages.Should().ContainSingle();
        secondMessages.Should().ContainSingle();
        secondMessages[0].StorageId.Should().NotBe(firstMessages[0].StorageId);
    }

    [Fact]
    public async Task should_pick_oldest_retry_rows_with_configured_batch_size()
    {
        // given
        var storage = _CreateStorage(new MessagingOptions { Version = "v1", RetryBatchSize = 2 });
        var serializer = GetSerializer();
        var now = DateTime.UtcNow;
        var oldestId = Guid.NewGuid();
        var middleId = Guid.NewGuid();
        var newestId = Guid.NewGuid();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);

            await _InsertPublishedRowAsync(
                connection,
                oldestId,
                serializer.Serialize(CreateMessage("sql-oldest")),
                StatusName.Failed,
                expiresAt: null,
                nextRetryAt: now.AddMinutes(-3)
            );
            await _InsertPublishedRowAsync(
                connection,
                middleId,
                serializer.Serialize(CreateMessage("sql-middle")),
                StatusName.Failed,
                expiresAt: null,
                nextRetryAt: now.AddMinutes(-2)
            );
            await _InsertPublishedRowAsync(
                connection,
                newestId,
                serializer.Serialize(CreateMessage("sql-newest")),
                StatusName.Failed,
                expiresAt: null,
                nextRetryAt: now.AddMinutes(-1)
            );
        }

        // when
        var picked = (await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)).ToList();

        // then
        picked.Select(message => message.StorageId).Should().BeEquivalentTo([oldestId, middleId]);
        picked.Should().NotContain(message => message.StorageId == newestId);
    }

    // -------------------------------------------------------------------------
    // Filtered-index shape verification — pins the SQL Server analog of the
    // PostgreSqlStorageTests partial-index test (`should_key_retry_pickup_index_on_version_then_next_retry_at`).
    // Regression to a different key order or missing filter predicate would silently
    // expand the index footprint and break the planner's ability to seek directly to
    // pickup-eligible rows.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Received", "IX_messaging_Received_Version_NextRetryAt")]
    [InlineData("Published", "IX_messaging_Published_Version_NextRetryAt")]
    public async Task should_key_retry_pickup_filtered_index_on_version_then_next_retry_at(
        string tableName,
        string indexName
    )
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // Key column order — Version must lead so it acts as a seek predicate, NextRetryAt
        // follows so range scans against the time predicate stay cheap.
        var columns = (
            await connection.QueryAsync<string>(
                """
                SELECT c.name
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                JOIN sys.objects o ON o.object_id = i.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE s.name = N'messaging'
                  AND o.name = @TableName
                  AND i.name = @IndexName
                  AND ic.is_included_column = 0
                ORDER BY ic.key_ordinal;
                """,
                new { TableName = tableName, IndexName = indexName }
            )
        ).ToList();

        columns
            .Should()
            .BeEquivalentTo(
                new[] { "Version", "NextRetryAt" },
                opts => opts.WithStrictOrdering(),
                "filtered-index key order must match the pickup query's seek path"
            );

        // Filtered predicate — must be NextRetryAt IS NOT NULL so terminal rows are physically
        // excluded from the index and the planner does not pay for them on every probe.
        var filterDefinition = await connection.QueryFirstOrDefaultAsync<string>(
            """
            SELECT i.filter_definition
            FROM sys.indexes i
            JOIN sys.objects o ON o.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = N'messaging'
              AND o.name = @TableName
              AND i.name = @IndexName;
            """,
            new { TableName = tableName, IndexName = indexName }
        );

        filterDefinition
            .Should()
            .NotBeNull("the retry-pickup index must be a filtered index, not a full nonclustered index")
            .And.Contain("NextRetryAt", "the filter must reference NextRetryAt")
            .And.Contain("IS NOT NULL", "the filter must exclude rows with NULL NextRetryAt");
    }

    [Theory]
    [InlineData("Received", "IX_messaging_Received_Owner_NotNull")]
    [InlineData("Published", "IX_messaging_Published_Owner_NotNull")]
    public async Task should_key_owner_filtered_index_on_owner_with_not_null_filter(string tableName, string indexName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var columns = (
            await connection.QueryAsync<string>(
                """
                SELECT c.name
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                JOIN sys.objects o ON o.object_id = i.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE s.name = N'messaging'
                  AND o.name = @TableName
                  AND i.name = @IndexName
                  AND ic.is_included_column = 0
                ORDER BY ic.key_ordinal;
                """,
                new { TableName = tableName, IndexName = indexName }
            )
        ).ToList();

        columns.Should().BeEquivalentTo(["Owner"], opts => opts.WithStrictOrdering());

        var filterDefinition = await connection.QueryFirstOrDefaultAsync<string>(
            """
            SELECT i.filter_definition
            FROM sys.indexes i
            JOIN sys.objects o ON o.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = N'messaging'
              AND o.name = @TableName
              AND i.name = @IndexName;
            """,
            new { TableName = tableName, IndexName = indexName }
        );

        filterDefinition
            .Should()
            .NotBeNull("the owner reclaim index must be filtered, not a full nonclustered index")
            .And.Contain("Owner", "the filter must reference Owner")
            .And.Contain("IS NOT NULL", "the filter must exclude rows without a Coordination owner");
    }

    private SqlServerDataStorage _CreateStorage(MessagingOptions messagingOptions)
    {
        messagingOptions.RetryPolicy.MaxPersistedRetries = 4;
        messagingOptions.FailedMessageExpiredAfter = 3600;

        var sqlServerOptions = Options.Create(
            new SqlServerOptions { ConnectionString = fixture.ConnectionString, Schema = "messaging" }
        );
        var initializer = new SqlServerStorageInitializer(
            NullLogger<SqlServerStorageInitializer>.Instance,
            sqlServerOptions,
            Options.Create(messagingOptions)
        );

        return new SqlServerDataStorage(
            Options.Create(messagingOptions),
            sqlServerOptions,
            initializer,
            GetSerializer(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            NodeMembership,
            NullLogger<SqlServerDataStorage>.Instance
        );
    }

    private static async Task _InsertPoisonRetryRowAsync(
        SqlConnection connection,
        string tableName,
        Guid id,
        DateTime now
    )
    {
        if (string.Equals(tableName, "Published", StringComparison.Ordinal))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO messaging.Published
                    (Id, Version, Name, Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId)
                VALUES
                    (@Id, 'v1', 'poison-published', 'not-json', 0, 0, @Now, NULL, @NextRetryAt, NULL, NULL, 'Failed', @MessageId);
                """,
                new
                {
                    Id = id,
                    Now = now,
                    NextRetryAt = now.AddMinutes(-1),
                    MessageId = $"poison-{id:N}",
                }
            );
            return;
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO messaging.Received
                (Id, Version, Name, [Group], Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId, ExceptionInfo)
            VALUES
                (@Id, 'v1', 'poison-received', 'poison-group', 'not-json', 0, 0, @Now, NULL, @NextRetryAt, NULL, NULL, 'Failed', @MessageId, NULL);
            """,
            new
            {
                Id = id,
                Now = now,
                NextRetryAt = now.AddMinutes(-1),
                MessageId = $"poison-{id:N}",
            }
        );
    }

    private static Task _InsertPublishedRowAsync(
        SqlConnection connection,
        Guid id,
        string content,
        StatusName statusName,
        DateTime? expiresAt,
        DateTime? nextRetryAt
    ) =>
        connection.ExecuteAsync(
            """
            INSERT INTO messaging.Published
                (Id, Version, Name, Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId)
            VALUES
                (@Id, 'v1', 'sql-provider-test', @Content, 0, 0, @Now, @ExpiresAt, @NextRetryAt, NULL, NULL, @StatusName, @MessageId);
            """,
            new
            {
                Id = id,
                Content = content,
                Now = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                NextRetryAt = nextRetryAt,
                StatusName = statusName.ToString("G"),
                MessageId = $"sql-{id:N}",
            }
        );

    private static Task _InsertHealthyRetryRowAsync(
        SqlConnection connection,
        string tableName,
        Guid id,
        string content,
        DateTime now
    )
    {
        if (string.Equals(tableName, "Published", StringComparison.Ordinal))
        {
            return connection.ExecuteAsync(
                """
                INSERT INTO messaging.Published
                    (Id, Version, Name, Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId)
                VALUES
                    (@Id, 'v1', 'healthy-published', @Content, 0, 0, @Now, NULL, @NextRetryAt, NULL, NULL, 'Failed', @MessageId);
                """,
                new
                {
                    Id = id,
                    Content = content,
                    Now = now,
                    NextRetryAt = now.AddMinutes(-1),
                    MessageId = $"healthy-{id:N}",
                }
            );
        }

        return connection.ExecuteAsync(
            """
            INSERT INTO messaging.Received
                (Id, Version, Name, [Group], Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId, ExceptionInfo)
            VALUES
                (@Id, 'v1', 'healthy-received', 'healthy-group', @Content, 0, 0, @Now, NULL, @NextRetryAt, NULL, NULL, 'Failed', @MessageId, NULL);
            """,
            new
            {
                Id = id,
                Content = content,
                Now = now,
                NextRetryAt = now.AddMinutes(-1),
                MessageId = $"healthy-{id:N}",
            }
        );
    }

    #endregion
}
