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
    private IOptions<SqlServerOptions>? _sqlServerOptions;
    private IOptions<MessagingOptions>? _messagingOptions;

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
    protected override IDataStorage CreateStorageWithTimeProvider(TimeProvider timeProvider)
    {
        _EnsureInitialized();
        return _CreateStorage(timeProvider);
    }

    /// <inheritdoc />
    protected override bool TrySetDispatchTimeout(TimeSpan dispatchTimeout)
    {
        _EnsureInitialized();
        _messagingOptions!.Value.RetryPolicy.DispatchTimeout = dispatchTimeout;
        return true;
    }

    /// <inheritdoc />
    protected override async Task<DateTime?> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");
    }

    /// <inheritdoc />
    protected override async Task<PersistedLeaseIdentity?> GetPersistedLeaseIdentityAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var tableName = published ? "Published" : "Received";
        return await connection.QuerySingleAsync<PersistedLeaseIdentity>(
            $"SELECT LockedUntil, Owner FROM messaging.{tableName} WHERE Id = @Id",
            new { Id = storageId }
        );
    }

    /// <inheritdoc />
    protected override async Task<Guid?> SeedUnsupportedLaneRetryRowAsync(
        IDataStorage storage,
        bool published,
        short rawLane,
        DateTimeOffset nextRetryAt,
        CancellationToken cancellationToken
    )
    {
        _EnsureInitialized();
        var id = Guid.NewGuid();
        var content = _serializer!.Serialize(CreateMessage($"unsupported-lane-{id:N}"));
        var tableName = published ? "Published" : "Received";
        var groupColumns = published ? string.Empty : ", [Group], ExceptionInfo";
        var groupValues = published ? string.Empty : ", 'unsupported-lane-group', NULL";
        var sql = $"""
            INSERT INTO messaging.{tableName}
                (Id, Version, Name, Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId{groupColumns})
            VALUES
                (@Id, 'v1', 'unsupported-lane', @Content, @IntentType, 0, @Added, NULL, @NextRetryAt, @LockedUntil, 'stale-unsupported-lane-owner', 'Failed', @MessageId{groupValues});
            """;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    Id = id,
                    Content = content,
                    IntentType = rawLane,
                    Added = TimeProvider.GetUtcNow(),
                    NextRetryAt = nextRetryAt,
                    LockedUntil = nextRetryAt,
                    MessageId = $"unsupported-lane-{id:N}",
                },
                cancellationToken: cancellationToken
            )
        );

        return id;
    }

    /// <inheritdoc />
    protected override async Task<PersistedPoisonRetryState?> GetPersistedPoisonRetryStateAsync(
        IDataStorage storage,
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        var tableName = published ? "Published" : "Received";
        var exceptionInfo = published ? "CAST(NULL AS nvarchar(max))" : "ExceptionInfo";
        var sql = $"""
            SELECT IntentType AS RawLane, StatusName, ExpiresAt, NextRetryAt, LockedUntil, Owner,
                   {exceptionInfo} AS ExceptionInfo
            FROM messaging.{tableName}
            WHERE Id=@Id;
            """;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<PersistedPoisonRetryState>(
            new CommandDefinition(sql, new { Id = storageId }, cancellationToken: cancellationToken)
        );
    }

    private IDataStorage _CreateStorage(TimeProvider timeProvider)
    {
        return new SqlServerDataStorage(
            _messagingOptions!,
            _sqlServerOptions!,
            _initializer!,
            _serializer!,
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            timeProvider,
            NodeMembership,
            NullLogger<SqlServerDataStorage>.Instance
        );
    }

    /// <inheritdoc />
    protected override IDataStorage CreateStorageWithRetryBatchSize(int retryBatchSize)
    {
        return _CreateStorage(new MessagingOptions { Version = "v1", RetryBatchSize = retryBatchSize });
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

        _sqlServerOptions = provider.GetRequiredService<IOptions<SqlServerOptions>>();
        _messagingOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();
        _serializer = provider.GetRequiredService<ISerializer>();

        _initializer = new SqlServerStorageInitializer(
            NullLogger<SqlServerStorageInitializer>.Instance,
            _sqlServerOptions,
            _messagingOptions
        );

        _storage = _CreateStorage(TimeProvider.System);
    }

    #region Data Storage Tests

    [Fact]
    public override Task should_initialize_schema()
    {
        return base.should_initialize_schema();
    }

    [Fact]
    public override Task should_get_table_names()
    {
        return base.should_get_table_names();
    }

    [Fact]
    public override Task should_converge_inbox_admission_and_require_exact_fence()
    {
        return base.should_converge_inbox_admission_and_require_exact_fence();
    }

    [Fact]
    public override Task should_converge_n_way_inbox_admission_on_one_generation()
    {
        return base.should_converge_n_way_inbox_admission_on_one_generation();
    }

    [Fact]
    public override Task should_isolate_every_persisted_inbox_key_component()
    {
        return base.should_isolate_every_persisted_inbox_key_component();
    }

    [Fact]
    public override Task should_enforce_inbox_key_length_boundaries_without_truncation()
    {
        return base.should_enforce_inbox_key_length_boundaries_without_truncation();
    }

    [Fact]
    public override Task should_suppress_terminal_inbox_redelivery_independent_of_topology_group()
    {
        return base.should_suppress_terminal_inbox_redelivery_independent_of_topology_group();
    }

    [Fact]
    public override Task should_apply_audited_inbox_operations_once_and_reject_operation_identity_reuse() =>
        base.should_apply_audited_inbox_operations_once_and_reject_operation_identity_reuse();

    [Fact]
    public async Task should_publish_final_inbox_schema_marker_and_key_index()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        var state = await connection.QuerySingleAsync<(
            int SchemaVersion,
            long IndexCount,
            long ConstraintCount,
            long ReceiptColumnCount
        )>(
            """
            SELECT state.SchemaVersion, (
                SELECT COUNT_BIG(*) FROM sys.indexes
                WHERE object_id=OBJECT_ID(N'messaging.Received') AND name=N'UX_messaging_Received_InboxKey'
            ) AS IndexCount, (
                SELECT COUNT_BIG(*) FROM sys.check_constraints WHERE name=N'CK_messaging_Received_InboxRetentionV3'
            ) AS ConstraintCount, (
                SELECT COUNT_BIG(*) FROM sys.columns
                WHERE object_id=OBJECT_ID(N'messaging.InboxOperationReceipts')
                  AND name IN(N'ExpectedStatus',N'Outcome',N'ChildIncarnationId')
            ) AS ReceiptColumnCount
            FROM messaging.SchemaState AS state
            WHERE state.Component=N'inbox';
            """
        );

        state.SchemaVersion.Should().Be(3);
        state.IndexCount.Should().Be(1);
        state.ConstraintCount.Should().Be(1);
        state.ReceiptColumnCount.Should().Be(3);
    }

    [Fact]
    public async Task should_preserve_cleanup_receipt_and_audit_after_inbox_row_deletion()
    {
        var storage = _storage!;
        var admitted = await storage.AdmitReceivedMessageAsync(
            "orders.created",
            "orders-group",
            "orders.cleanup",
            "v1",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage($"cleanup-{Guid.NewGuid():N}", "orders.created"),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            cancellationToken: AbortToken
        );
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            UPDATE messaging.Received SET StatusName=N'Failed',NextRetryAt=NULL,TerminalAt=DATEADD(day,-2,SYSDATETIMEOFFSET()),EffectiveExpiresAt=DATEADD(day,-1,SYSDATETIMEOFFSET())
            WHERE Id=@Id;
            """,
            new { Id = admitted.Message.StorageId }
        );

        (
            await storage.DeleteExpiresAsync(
                _initializer!.GetReceivedTableName(),
                DateTimeOffset.UtcNow,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .Be(1);
        var persisted = await connection.QuerySingleAsync<(long Rows, long Receipts, long Audits)>(
            """
            SELECT
              (SELECT COUNT_BIG(*) FROM messaging.Received WHERE Id=@Id) AS Rows,
              (SELECT COUNT_BIG(*) FROM messaging.InboxOperationReceipts WHERE StorageId=@Id AND OperationType=N'Cleanup') AS Receipts,
              (SELECT COUNT_BIG(*) FROM messaging.InboxAudit a JOIN messaging.InboxOperationReceipts r ON r.OperationId=a.OperationId WHERE r.StorageId=@Id AND a.OperationType=N'Cleanup') AS Audits;
            """,
            new { Id = admitted.Message.StorageId }
        );
        persisted.Should().Be((0, 1, 1));
    }

    [Fact]
    public async Task should_fail_closed_when_inbox_schema_is_newer_than_supported()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync("UPDATE messaging.SchemaState SET SchemaVersion=4 WHERE Component=N'inbox';");

        try
        {
            var act = async () => await GetInitializer().InitializeAsync(AbortToken);

            await act.Should().ThrowAsync<SqlException>().WithMessage("*newer than supported version 3*");
            (
                await connection.ExecuteScalarAsync<int>(
                    "SELECT SchemaVersion FROM messaging.SchemaState WHERE Component=N'inbox';"
                )
            )
                .Should()
                .Be(4, "a rejected older binary must not rewrite the newer readiness marker");
        }
        finally
        {
            await connection.ExecuteAsync("UPDATE messaging.SchemaState SET SchemaVersion=3 WHERE Component=N'inbox';");
        }
    }

    [Fact]
    public override Task should_store_published_message()
    {
        return base.should_store_published_message();
    }

    [Fact]
    public override Task should_store_scheduled_message_with_atomic_not_before_state()
    {
        return base.should_store_scheduled_message_with_atomic_not_before_state();
    }

    [Fact]
    public override Task should_store_published_message_with_non_numeric_message_id()
    {
        return base.should_store_published_message_with_non_numeric_message_id();
    }

    [Fact]
    public override Task should_store_published_message_with_intent_type()
    {
        return base.should_store_published_message_with_intent_type();
    }

    [Fact]
    public override Task should_store_received_message()
    {
        return base.should_store_received_message();
    }

    [Fact]
    public override Task should_store_received_exception_message()
    {
        return base.should_store_received_exception_message();
    }

    [Fact]
    public override Task should_change_publish_state()
    {
        return base.should_change_publish_state();
    }

    [Fact]
    public override Task should_change_receive_state()
    {
        return base.should_change_receive_state();
    }

    [Fact]
    public override Task should_preserve_persisted_envelope_when_published_transition_declares_preserve()
    {
        return base.should_preserve_persisted_envelope_when_published_transition_declares_preserve();
    }

    [Fact]
    public override Task should_preserve_persisted_envelope_when_received_transition_declares_preserve()
    {
        return base.should_preserve_persisted_envelope_when_received_transition_declares_preserve();
    }

    [Fact]
    public override Task should_refresh_persisted_envelope_when_published_transition_declares_refresh()
    {
        return base.should_refresh_persisted_envelope_when_published_transition_declares_refresh();
    }

    [Fact]
    public override Task should_refresh_persisted_envelope_when_received_transition_declares_refresh()
    {
        return base.should_refresh_persisted_envelope_when_received_transition_declares_refresh();
    }

    [Fact]
    public override Task should_change_publish_state_to_delayed()
    {
        return base.should_change_publish_state_to_delayed();
    }

    [Fact]
    public override Task should_not_flip_terminal_published_row_back_to_delayed()
    {
        return base.should_not_flip_terminal_published_row_back_to_delayed();
    }

    [Fact]
    public override Task should_ignore_unknown_storage_ids_when_flushing_delayed_state()
    {
        return base.should_ignore_unknown_storage_ids_when_flushing_delayed_state();
    }

    [Fact]
    public override Task should_get_published_messages_of_need_retry()
    {
        return base.should_get_published_messages_of_need_retry();
    }

    [Fact]
    public override Task should_get_received_messages_of_need_retry()
    {
        return base.should_get_received_messages_of_need_retry();
    }

    [Fact]
    public override Task should_claim_published_retry_messages_by_lane_and_apply_batch_per_lane()
    {
        return base.should_claim_published_retry_messages_by_lane_and_apply_batch_per_lane();
    }

    [Fact]
    public override Task should_claim_received_retry_messages_by_lane_and_apply_batch_per_lane()
    {
        return base.should_claim_received_retry_messages_by_lane_and_apply_batch_per_lane();
    }

    [Fact]
    public override Task should_preserve_unsupported_lane_without_starving_published_retry()
    {
        return base.should_preserve_unsupported_lane_without_starving_published_retry();
    }

    [Fact]
    public override Task should_preserve_unsupported_lane_without_starving_received_retry()
    {
        return base.should_preserve_unsupported_lane_without_starving_received_retry();
    }

    [Fact]
    public override Task should_delete_expired_messages()
    {
        return base.should_delete_expired_messages();
    }

    [Fact]
    public override Task should_not_delete_expired_failed_messages_with_pending_retry()
    {
        return base.should_not_delete_expired_failed_messages_with_pending_retry();
    }

    [Fact]
    public override Task should_delete_published_message()
    {
        return base.should_delete_published_message();
    }

    [Fact]
    public override Task should_delete_received_message()
    {
        return base.should_delete_received_message();
    }

    [Fact]
    public override Task should_get_monitoring_api()
    {
        return base.should_get_monitoring_api();
    }

    [Fact]
    public override Task should_handle_concurrent_storage_operations()
    {
        return base.should_handle_concurrent_storage_operations();
    }

    [Fact]
    public override Task should_schedule_messages_of_delayed()
    {
        return base.should_schedule_messages_of_delayed();
    }

    [Fact]
    public override Task should_claim_delayed_messages_atomically_when_capability_supported()
    {
        return base.should_claim_delayed_messages_atomically_when_capability_supported();
    }

    [Fact]
    public override Task should_keep_early_delayed_claim_lease_alive_until_dispatch()
    {
        return base.should_keep_early_delayed_claim_lease_alive_until_dispatch();
    }

    [Fact]
    public override Task should_clear_claim_lease_when_flushing_delayed_state()
    {
        return base.should_clear_claim_lease_when_flushing_delayed_state();
    }

    [Fact]
    public override Task should_return_disjoint_winners_to_concurrent_delayed_claimers()
    {
        return base.should_return_disjoint_winners_to_concurrent_delayed_claimers();
    }

    [Fact]
    public override Task should_store_message_with_transaction()
    {
        return base.should_store_message_with_transaction();
    }

    [Fact]
    public async Task should_commit_scheduled_message_and_not_before_state_in_one_transaction()
    {
        var storage = GetStorage();
        var publishAt = TimeProvider.System.GetUtcNow().AddMinutes(30);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);

        var stored = await storage.StoreScheduledMessageAsync(
            "scheduled-transaction-commit",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            publishAt,
            transaction,
            AbortToken
        );
        await transaction.CommitAsync(AbortToken);

        var retrieved = await storage.GetMonitoringApi().GetPublishedMessageAsync(stored.StorageId, AbortToken);
        retrieved.Should().NotBeNull();
        retrieved!.ExpiresAt.Should().BeCloseTo(publishAt, TimeSpan.FromSeconds(1));
        retrieved.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public async Task should_rollback_scheduled_message_and_not_before_state_together()
    {
        var storage = GetStorage();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);

        var stored = await storage.StoreScheduledMessageAsync(
            "scheduled-transaction-rollback",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            TimeProvider.System.GetUtcNow().AddMinutes(30),
            transaction,
            AbortToken
        );
        await transaction.RollbackAsync(AbortToken);

        var retrieved = await storage.GetMonitoringApi().GetPublishedMessageAsync(stored.StorageId, AbortToken);
        retrieved.Should().BeNull();
    }

    [Fact]
    public override Task should_handle_message_state_transitions()
    {
        return base.should_handle_message_state_transitions();
    }

    [Fact]
    public override Task should_handle_failed_message_state()
    {
        return base.should_handle_failed_message_state();
    }

    [Fact]
    public override Task should_not_return_published_message_with_failed_status_and_null_next_retry_at()
    {
        return base.should_not_return_published_message_with_failed_status_and_null_next_retry_at();
    }

    [Fact]
    public override Task should_seal_succeeded_published_message_against_state_change_and_retry_pickup()
    {
        return base.should_seal_succeeded_published_message_against_state_change_and_retry_pickup();
    }

    [Fact]
    public override Task should_not_return_published_message_with_future_next_retry_at()
    {
        return base.should_not_return_published_message_with_future_next_retry_at();
    }

    [Fact]
    public override Task should_not_return_received_message_with_failed_status_and_null_next_retry_at()
    {
        return base.should_not_return_received_message_with_failed_status_and_null_next_retry_at();
    }

    [Fact]
    public override Task should_not_return_received_message_with_future_next_retry_at()
    {
        return base.should_not_return_received_message_with_future_next_retry_at();
    }

    [Fact]
    public override Task should_not_return_leased_published_message_until_lease_expires()
    {
        return base.should_not_return_leased_published_message_until_lease_expires();
    }

    [Fact]
    public override Task should_use_database_clock_when_reclaiming_published_retry_lease()
    {
        return base.should_use_database_clock_when_reclaiming_published_retry_lease();
    }

    [Fact]
    public override Task should_use_database_clock_when_reclaiming_received_retry_lease()
    {
        return base.should_use_database_clock_when_reclaiming_received_retry_lease();
    }

    [Fact]
    public override Task should_use_database_clock_when_fast_forwarding_dead_owner_lease()
    {
        return base.should_use_database_clock_when_fast_forwarding_dead_owner_lease();
    }

    [Fact]
    public override Task should_stamp_retry_lease_from_database_clock()
    {
        return base.should_stamp_retry_lease_from_database_clock();
    }

    [Fact]
    public override Task should_use_application_clock_when_scheduling_published_retry()
    {
        return base.should_use_application_clock_when_scheduling_published_retry();
    }

    [Fact]
    public override Task should_use_application_clock_when_scheduling_received_retry()
    {
        return base.should_use_application_clock_when_scheduling_received_retry();
    }

    [Fact]
    public async Task should_preserve_sub_second_retry_lease_precision()
    {
        _EnsureInitialized();
        _messagingOptions!.Value.RetryPolicy.DispatchTimeout = TimeSpan.FromMilliseconds(500);
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "sub-second-retry-lease",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );

        var beforeClaim = DateTimeOffset.UtcNow;
        var claimed = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == storedMessage.StorageId)
            .Subject;

        claimed.LockedUntil.Should().BeAfter(beforeClaim.AddMilliseconds(100));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public override Task should_stamp_fresh_dispatch_lease_from_database_clock(bool published, bool reserveAttempt)
    {
        return base.should_stamp_fresh_dispatch_lease_from_database_clock(published, reserveAttempt);
    }

    [Fact]
    public override Task should_reject_mismatched_original_retries()
    {
        return base.should_reject_mismatched_original_retries();
    }

    [Fact]
    public override Task should_lease_and_reserve_publish_attempt_in_single_step()
    {
        return base.should_lease_and_reserve_publish_attempt_in_single_step();
    }

    [Fact]
    public override Task should_reject_lease_and_reserve_with_stale_inline_attempts_token()
    {
        return base.should_reject_lease_and_reserve_with_stale_inline_attempts_token();
    }

    [Fact]
    public override Task should_reject_stale_published_lease_generation_writes()
    {
        return base.should_reject_stale_published_lease_generation_writes();
    }

    [Fact]
    public override Task should_reject_stale_received_lease_generation_writes()
    {
        return base.should_reject_stale_received_lease_generation_writes();
    }

    [Fact]
    public override Task should_allow_published_fenced_writes_with_fast_application_clock()
    {
        return base.should_allow_published_fenced_writes_with_fast_application_clock();
    }

    [Fact]
    public override Task should_allow_received_fenced_writes_with_fast_application_clock()
    {
        return base.should_allow_received_fenced_writes_with_fast_application_clock();
    }

    [Fact]
    public override Task should_report_false_when_received_exception_message_is_already_terminal()
    {
        return base.should_report_false_when_received_exception_message_is_already_terminal();
    }

    [Fact]
    public override Task should_handle_concurrent_redelivery_storm_on_same_message_id()
    {
        return base.should_handle_concurrent_redelivery_storm_on_same_message_id();
    }

    [Fact]
    public override Task should_handle_concurrent_first_insert_storm_with_null_and_non_null_group()
    {
        return base.should_handle_concurrent_first_insert_storm_with_null_and_non_null_group();
    }

    [Fact]
    public override Task should_handle_concurrent_store_received_message_with_same_identity()
    {
        return base.should_handle_concurrent_store_received_message_with_same_identity();
    }

    [Fact]
    public override Task should_pickup_message_at_max_persisted_retries_and_exclude_above()
    {
        return base.should_pickup_message_at_max_persisted_retries_and_exclude_above();
    }

    [Fact]
    public override Task should_not_return_leased_received_message_until_lease_expires()
    {
        return base.should_not_return_leased_received_message_until_lease_expires();
    }

    [Fact]
    public override Task should_return_unstored_snapshot_when_redelivery_hits_active_receive_lease()
    {
        return base.should_return_unstored_snapshot_when_redelivery_hits_active_receive_lease();
    }

    [Fact]
    public override Task should_handle_concurrent_state_updates_to_same_row()
    {
        return base.should_handle_concurrent_state_updates_to_same_row();
    }

    [Fact]
    public override Task should_reclaim_published_retry_row_owned_by_dead_node()
    {
        return base.should_reclaim_published_retry_row_owned_by_dead_node();
    }

    [Fact]
    public override Task should_reclaim_received_retry_row_owned_by_dead_node()
    {
        return base.should_reclaim_received_retry_row_owned_by_dead_node();
    }

    [Fact]
    public override Task should_stamp_owner_on_claim()
    {
        return base.should_stamp_owner_on_claim();
    }

    [Fact]
    public override Task should_release_only_exact_published_retry_lease_generation()
    {
        return base.should_release_only_exact_published_retry_lease_generation();
    }

    [Fact]
    public override Task should_release_only_exact_received_retry_lease_generation()
    {
        return base.should_release_only_exact_received_retry_lease_generation();
    }

    [Fact]
    public override Task should_atomically_defer_only_exact_live_received_retry_lease_generation()
    {
        return base.should_atomically_defer_only_exact_live_received_retry_lease_generation();
    }

    [Fact]
    public override Task should_atomically_defer_received_retry_lease_with_null_owner()
    {
        return base.should_atomically_defer_received_retry_lease_with_null_owner();
    }

    [Fact]
    public override Task should_not_defer_expired_received_retry_lease()
    {
        return base.should_not_defer_expired_received_retry_lease();
    }

    [Fact]
    public override Task should_not_defer_terminal_received_retry_lease()
    {
        return base.should_not_defer_terminal_received_retry_lease();
    }

    [Fact]
    public override Task should_not_release_terminal_retry_lease_generation()
    {
        return base.should_not_release_terminal_retry_lease_generation();
    }

    [Fact]
    public override Task should_batch_release_only_exact_published_retry_lease_generations()
    {
        return base.should_batch_release_only_exact_published_retry_lease_generations();
    }

    [Fact]
    public override Task should_not_reclaim_rows_of_live_or_restarted_incarnation()
    {
        return base.should_not_reclaim_rows_of_live_or_restarted_incarnation();
    }

    [Fact]
    public override Task should_not_reclaim_terminal_rows()
    {
        return base.should_not_reclaim_terminal_rows();
    }

    [Fact]
    public override Task should_be_inert_when_no_dead_owners_passed()
    {
        return base.should_be_inert_when_no_dead_owners_passed();
    }

    [Fact]
    public override Task should_not_reclaim_rows_with_null_owner()
    {
        return base.should_not_reclaim_rows_with_null_owner();
    }

    [Fact]
    public override Task should_reclaim_dead_owner_rows_idempotently()
    {
        return base.should_reclaim_dead_owner_rows_idempotently();
    }

    [Fact]
    public override Task should_not_reclaim_dead_owner_rows_with_expired_lease()
    {
        return base.should_not_reclaim_dead_owner_rows_with_expired_lease();
    }

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

    [Fact]
    public async Task transactional_inbox_completion_should_disappear_when_provider_transaction_rolls_back()
    {
        var storage = GetStorage();
        var origin = CreateMessage($"transactional-rollback-{Guid.NewGuid():N}", "orders.created");
        var admission = await storage.AdmitReceivedMessageAsync(
            "orders.created",
            "orders-topology-a",
            "orders.consumer",
            "v1",
            new MediumMessage
            {
                StorageId = Guid.NewGuid(),
                Origin = origin,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            generation: 0,
            cancellationToken: AbortToken
        );
        var message = admission.Message;
        var originalInlineAttempts = message.InlineAttempts++;
        NodeMembership.SetIdentity("sqlserver-transactional-inbox");
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                message,
                TimeSpan.FromMinutes(1),
                originalInlineAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        (await ((ITransactionalInboxStorage)storage).CompleteReceivedInboxAsync(message, transaction, AbortToken))
            .Should()
            .BeTrue();
        var succeededInside = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                """SELECT CAST(CASE WHEN StatusName='Succeeded' THEN 1 ELSE 0 END AS bit) FROM messaging.Received WHERE Id=@Id""",
                new { Id = message.StorageId },
                transaction,
                cancellationToken: AbortToken
            )
        );
        succeededInside.Should().BeTrue();

        await transaction.RollbackAsync(AbortToken);

        var succeededOutside = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                """SELECT CAST(CASE WHEN StatusName='Succeeded' THEN 1 ELSE 0 END AS bit) FROM messaging.Received WHERE Id=@Id""",
                new { Id = message.StorageId },
                cancellationToken: AbortToken
            )
        );
        succeededOutside.Should().BeFalse();
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
    public async Task should_skip_unknown_lane_without_starving_concurrent_lane_claims(string tableName)
    {
        var busStorage = _CreateStorage(new MessagingOptions { Version = "v1", RetryBatchSize = 1 });
        var queueStorage = _CreateStorage(new MessagingOptions { Version = "v1", RetryBatchSize = 1 });
        var serializer = GetSerializer();
        var now = TimeProvider.GetUtcNow();
        var published = string.Equals(tableName, "Published", StringComparison.Ordinal);
        var unknownId = (
            await SeedUnsupportedLaneRetryRowAsync(
                busStorage,
                published,
                rawLane: 77,
                nextRetryAt: now.AddMinutes(-5),
                AbortToken
            )
        )!.Value;
        var betweenUnknownId = (
            await SeedUnsupportedLaneRetryRowAsync(
                busStorage,
                published,
                rawLane: 78,
                nextRetryAt: now.AddMinutes(-3).AddSeconds(-30),
                AbortToken
            )
        )!.Value;
        var busId = Guid.NewGuid();
        var queueId = Guid.NewGuid();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await _InsertHealthyRetryRowAsync(
                connection,
                tableName,
                busId,
                serializer.Serialize(CreateMessage("healthy-bus-retry")),
                now.AddMinutes(-4),
                rawLane: 0
            );
            await _InsertHealthyRetryRowAsync(
                connection,
                tableName,
                queueId,
                serializer.Serialize(CreateMessage("healthy-queue-retry")),
                now.AddMinutes(-3),
                rawLane: 1
            );
        }

        var unknownBefore = await GetPersistedPoisonRetryStateAsync(busStorage, published, unknownId, AbortToken);
        var betweenUnknownBefore = await GetPersistedPoisonRetryStateAsync(
            busStorage,
            published,
            betweenUnknownId,
            AbortToken
        );

        var claims = await Task.WhenAll(
            _ClaimRetryAsync(busStorage, published, MessageLane.Bus),
            _ClaimRetryAsync(queueStorage, published, MessageLane.Queue)
        );

        claims[0].Select(message => message.StorageId).Should().Equal(busId);
        claims[1].Select(message => message.StorageId).Should().Equal(queueId);
        var unknownAfter = await GetPersistedPoisonRetryStateAsync(busStorage, published, unknownId, AbortToken);
        unknownAfter.Should().Be(unknownBefore, "automatic pickup must not mutate unknown lanes");
        var betweenUnknownAfter = await GetPersistedPoisonRetryStateAsync(
            busStorage,
            published,
            betweenUnknownId,
            AbortToken
        );
        betweenUnknownAfter
            .Should()
            .Be(betweenUnknownBefore, "unknown lanes between valid rows must not consume claim capacity or be mutated");

        await using (var repairConnection = new SqlConnection(fixture.ConnectionString))
        {
            await repairConnection.OpenAsync(AbortToken);
            await repairConnection.ExecuteAsync(
                $"UPDATE messaging.{tableName} SET IntentType = 1, LockedUntil = NULL, Owner = NULL WHERE Id = @Id",
                new { Id = unknownId }
            );
        }

        var repaired = await _ClaimRetryAsync(queueStorage, published, MessageLane.Queue);
        repaired.Select(message => message.StorageId).Should().Equal(unknownId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_not_reclaim_dead_owner_lease_on_unknown_lane_rows(bool published)
    {
        const string deadOwner = "stale-unsupported-lane-owner";
        var storage = GetStorage();
        var id = (
            await SeedUnsupportedLaneRetryRowAsync(
                storage,
                published,
                rawLane: 77,
                nextRetryAt: TimeProvider.GetUtcNow().AddMinutes(5),
                AbortToken
            )
        )!.Value;
        var before = await GetPersistedPoisonRetryStateAsync(storage, published, id, AbortToken);

        var reclaimed = published
            ? await storage.ReclaimDeadPublishedOwnersAsync([deadOwner], AbortToken)
            : await storage.ReclaimDeadReceivedOwnersAsync([deadOwner], AbortToken);

        reclaimed.Should().Be(0);
        (await GetPersistedPoisonRetryStateAsync(storage, published, id, AbortToken))
            .Should()
            .Be(before, "automatic reclaim must not mutate unknown lanes");
    }

    [Fact]
    public async Task should_preserve_unknown_lanes_during_expiry_and_delayed_maintenance()
    {
        var storage = _CreateStorage(new MessagingOptions { Version = "v1", SchedulerBatchSize = 2 });
        var serializer = GetSerializer();
        var now = TimeProvider.GetUtcNow();
        var unknownExpiredId = Guid.NewGuid();
        var unknownDelayedId = Guid.NewGuid();
        var unknownQueuedId = Guid.NewGuid();
        var validDelayedId = Guid.NewGuid();
        var validQueuedId = Guid.NewGuid();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await _InsertPublishedRowAsync(
                connection,
                unknownExpiredId,
                serializer.Serialize(CreateMessage("unknown-expired")),
                StatusName.Failed,
                expiresAt: now.AddHours(-2),
                nextRetryAt: null,
                rawLane: 70
            );
            await _InsertPublishedRowAsync(
                connection,
                unknownDelayedId,
                serializer.Serialize(CreateMessage("unknown-delayed")),
                StatusName.Delayed,
                expiresAt: now.AddMinutes(-30),
                nextRetryAt: null,
                rawLane: 71
            );
            await _InsertPublishedRowAsync(
                connection,
                unknownQueuedId,
                serializer.Serialize(CreateMessage("unknown-queued")),
                StatusName.Queued,
                expiresAt: now.AddMinutes(-20),
                nextRetryAt: null,
                rawLane: 72
            );
            await _InsertPublishedRowAsync(
                connection,
                validDelayedId,
                serializer.Serialize(CreateMessage("valid-delayed")),
                StatusName.Delayed,
                expiresAt: now.AddMinutes(-10),
                nextRetryAt: null,
                rawLane: 0
            );
            await _InsertPublishedRowAsync(
                connection,
                validQueuedId,
                serializer.Serialize(CreateMessage("valid-queued")),
                StatusName.Queued,
                expiresAt: now.AddMinutes(-5),
                nextRetryAt: null,
                rawLane: 1
            );
        }

        var scheduled = new List<MediumMessage>();
        await storage.ScheduleMessagesOfDelayedAsync(
            (_, messages) =>
            {
                scheduled.AddRange(messages);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );
        var claimed = await storage.ClaimDelayedMessagesAsync(AbortToken);
        var deleted = await storage.DeleteExpiresAsync(
            "messaging.Published",
            now.AddHours(-1),
            batchCount: 10,
            AbortToken
        );

        scheduled.Select(message => message.StorageId).Should().BeEquivalentTo([validDelayedId, validQueuedId]);
        claimed.Select(message => message.StorageId).Should().BeEquivalentTo([validDelayedId, validQueuedId]);
        deleted.Should().Be(0);

        await using var assertConnection = new SqlConnection(fixture.ConnectionString);
        var unknownRows = (
            await assertConnection.QueryAsync<(
                Guid Id,
                short IntentType,
                string StatusName,
                DateTimeOffset? LockedUntil
            )>(
                """
                SELECT Id, IntentType, StatusName, LockedUntil
                FROM messaging.Published
                WHERE Id IN @Ids
                ORDER BY Id;
                """,
                new { Ids = new[] { unknownExpiredId, unknownDelayedId, unknownQueuedId } }
            )
        ).ToList();
        unknownRows.Should().HaveCount(3);
        unknownRows.Should().AllSatisfy(row => row.LockedUntil.Should().BeNull());
        unknownRows.Single(row => row.Id == unknownExpiredId).IntentType.Should().Be(70);
        unknownRows.Single(row => row.Id == unknownDelayedId).StatusName.Should().Be(nameof(StatusName.Delayed));
        unknownRows.Single(row => row.Id == unknownQueuedId).StatusName.Should().Be(nameof(StatusName.Queued));
    }

    [Theory]
    [InlineData("Published")]
    [InlineData("Received")]
    public async Task should_terminalize_poison_retry_row_when_content_cannot_deserialize(string tableName)
    {
        // given
        var storage = GetStorage();
        var id = Guid.NewGuid();
        var now = TimeProvider.GetUtcNow();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await _InsertPoisonRetryRowAsync(connection, tableName, id, now);
        }

        // when
        var picked = string.Equals(tableName, "Published", StringComparison.Ordinal)
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);

        // then
        picked.Should().NotContain(message => message.StorageId == id);

        await using var assertConnection = new SqlConnection(fixture.ConnectionString);
        await assertConnection.OpenAsync(AbortToken);

        var statusName = await assertConnection.ExecuteScalarAsync<string>(
            $"SELECT StatusName FROM messaging.{tableName} WHERE Id = @Id",
            new { Id = id }
        );
        var nextRetryAt = await assertConnection.ExecuteScalarAsync<DateTimeOffset?>(
            $"SELECT NextRetryAt FROM messaging.{tableName} WHERE Id = @Id",
            new { Id = id }
        );
        var lockedUntil = await assertConnection.ExecuteScalarAsync<DateTimeOffset?>(
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
        var now = TimeProvider.GetUtcNow();

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
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);

        picked.Select(message => message.StorageId).Should().Contain(healthyId).And.NotContain(poisonId);

        await using var assertConnection = new SqlConnection(fixture.ConnectionString);
        await assertConnection.OpenAsync(AbortToken);
        var poisonNextRetryAt = await assertConnection.ExecuteScalarAsync<DateTimeOffset?>(
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
        var now = DateTimeOffset.UtcNow;
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
        var now = DateTimeOffset.UtcNow;

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
        var now = DateTimeOffset.UtcNow;
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
        var picked = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();

        // then
        picked.Select(message => message.StorageId).Should().BeEquivalentTo([oldestId, middleId]);
        picked.Should().NotContain(message => message.StorageId == newestId);
    }

    [Fact]
    public async Task should_claim_received_recovery_when_read_committed_snapshot_is_enabled()
    {
        var databaseName = $"headless_messaging_rcsi_{Guid.NewGuid():N}";
        var masterBuilder = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = "master",
            Pooling = false,
        };
        var databaseBuilder = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = databaseName,
            Pooling = false,
        };

        await using var master = new SqlConnection(masterBuilder.ConnectionString);
        await master.OpenAsync(AbortToken);
        await master.ExecuteAsync($"CREATE DATABASE [{databaseName}];");

        try
        {
            await master.ExecuteAsync(
                $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;"
            );
            var messagingOptions = new MessagingOptions { Version = "v1", RetryBatchSize = 1 };
            var sqlServerOptions = Options.Create(
                new SqlServerOptions { ConnectionString = databaseBuilder.ConnectionString, Schema = "messaging" }
            );
            var initializer = new SqlServerStorageInitializer(
                NullLogger<SqlServerStorageInitializer>.Instance,
                sqlServerOptions,
                Options.Create(messagingOptions)
            );
            await initializer.InitializeAsync(AbortToken);
            var storage = _CreateStorage(messagingOptions, databaseBuilder.ConnectionString);

            var retryId = Guid.NewGuid();
            await using (var connection = new SqlConnection(databaseBuilder.ConnectionString))
            {
                await connection.OpenAsync(AbortToken);
                (
                    await connection.ExecuteScalarAsync<bool>(
                        "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name=DB_NAME();"
                    )
                )
                    .Should()
                    .BeTrue();
                await _InsertHealthyRetryRowAsync(
                    connection,
                    "Received",
                    retryId,
                    GetSerializer().Serialize(CreateMessage("sql-rcsi-recovery")),
                    DateTimeOffset.UtcNow.AddMinutes(-1)
                );
            }

            var claimed = await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);

            claimed.Should().ContainSingle(message => message.StorageId == retryId);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await master.ExecuteAsync(
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];"
            );
        }
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
    public async Task should_key_retry_pickup_filtered_index_on_version_lane_then_next_retry_at(
        string tableName,
        string indexName
    )
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // Key column order — equality predicates lead; NextRetryAt remains the trailing range key.
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
                new[] { "Version", "IntentType", "NextRetryAt" },
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
        return _CreateStorage(messagingOptions, fixture.ConnectionString);
    }

    private SqlServerDataStorage _CreateStorage(MessagingOptions messagingOptions, string connectionString)
    {
        messagingOptions.RetryPolicy.MaxPersistedRetries = 4;
        messagingOptions.FailedMessageExpiredAfter = 3600;

        var sqlServerOptions = Options.Create(
            new SqlServerOptions { ConnectionString = connectionString, Schema = "messaging" }
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
        DateTimeOffset now
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
        DateTimeOffset? expiresAt,
        DateTimeOffset? nextRetryAt,
        short rawLane = 0
    )
    {
        return connection.ExecuteAsync(
            """
            INSERT INTO messaging.Published
                (Id, Version, Name, Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId)
            VALUES
                (@Id, 'v1', 'sql-provider-test', @Content, @IntentType, 0, @Now, @ExpiresAt, @NextRetryAt, NULL, NULL, @StatusName, @MessageId);
            """,
            new
            {
                Id = id,
                Content = content,
                Now = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt,
                NextRetryAt = nextRetryAt,
                IntentType = rawLane,
                StatusName = statusName.ToString("G"),
                MessageId = $"sql-{id:N}",
            }
        );
    }

    private static Task _InsertHealthyRetryRowAsync(
        SqlConnection connection,
        string tableName,
        Guid id,
        string content,
        DateTimeOffset nextRetryAt,
        short rawLane = 0
    )
    {
        if (string.Equals(tableName, "Published", StringComparison.Ordinal))
        {
            return connection.ExecuteAsync(
                """
                INSERT INTO messaging.Published
                    (Id, Version, Name, Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId)
                VALUES
                    (@Id, 'v1', 'healthy-published', @Content, @IntentType, 0, @Now, NULL, @NextRetryAt, NULL, NULL, 'Failed', @MessageId);
                """,
                new
                {
                    Id = id,
                    Content = content,
                    Now = nextRetryAt,
                    NextRetryAt = nextRetryAt,
                    IntentType = rawLane,
                    MessageId = $"healthy-{id:N}",
                }
            );
        }

        return connection.ExecuteAsync(
            """
            INSERT INTO messaging.Received
                (Id, Version, Name, [Group], Content, IntentType, Retries, Added, ExpiresAt, NextRetryAt, LockedUntil, Owner, StatusName, MessageId, ExceptionInfo)
            VALUES
                (@Id, 'v1', 'healthy-received', 'healthy-group', @Content, @IntentType, 0, @Now, NULL, @NextRetryAt, NULL, NULL, 'Failed', @MessageId, NULL);
            """,
            new
            {
                Id = id,
                Content = content,
                Now = nextRetryAt,
                NextRetryAt = nextRetryAt,
                IntentType = rawLane,
                MessageId = $"healthy-{id:N}",
            }
        );
    }

    private static async Task<IReadOnlyList<MediumMessage>> _ClaimRetryAsync(
        IDataStorage storage,
        bool published,
        MessageLane lane
    )
    {
        var messages = published
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(lane, AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(lane, AbortToken);
        return messages.ToList();
    }

    #endregion
}
