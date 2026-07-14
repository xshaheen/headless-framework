// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Tests.Capabilities;

namespace Tests;

/// <summary>
/// Integration tests for PostgreSQL data storage using real PostgreSQL container.
/// Inherits from <see cref="DataStorageTestsBase"/> to run standard storage tests.
/// </summary>
[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlStorageTests(PostgreSqlTestFixture fixture) : DataStorageTestsBase
{
    private IStorageInitializer? _initializer;
    private IDataStorage? _storage;
    private ISerializer? _serializer;
    private IOptions<PostgreSqlOptions>? _postgreSqlOptions;
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
    protected override async Task<DateTime?> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<DateTime>("SELECT statement_timestamp()");
    }

    /// <inheritdoc />
    protected override async Task<PersistedLeaseIdentity?> GetPersistedLeaseIdentityAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var tableName = published ? "published" : "received";
        return await connection.QuerySingleAsync<PersistedLeaseIdentity>(
            $"""SELECT "LockedUntil", "Owner" FROM messaging.{tableName} WHERE "Id"=@Id""",
            new { Id = storageId }
        );
    }

    private IDataStorage _CreateStorage(TimeProvider timeProvider)
    {
        return new PostgreSqlDataStorage(
            _postgreSqlOptions!,
            _messagingOptions!,
            _initializer!,
            _serializer!,
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            timeProvider,
            NodeMembership,
            NullLogger<PostgreSqlDataStorage>.Instance
        );
    }

    /// <inheritdoc />
    protected override async Task<int> CountReceivedMessagesByIdentityAsync(
        string messageId,
        string? group,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sqlWithGroup =
            "SELECT COUNT(*) FROM messaging.received WHERE \"MessageId\" = @MessageId AND \"Group\" = @Group";
        const string sqlWithoutGroup =
            "SELECT COUNT(*) FROM messaging.received WHERE \"MessageId\" = @MessageId AND \"Group\" IS NULL";

        var rowCount = group is null
            ? await connection.ExecuteScalarAsync<long>(sqlWithoutGroup, new { MessageId = messageId })
            : await connection.ExecuteScalarAsync<long>(sqlWithGroup, new { MessageId = messageId, Group = group });

        return (int)rowCount;
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
        // Clean up tables after tests (ignore errors if schema doesn't exist)
        try
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    TRUNCATE TABLE messaging.published;
                    TRUNCATE TABLE messaging.received;
                    """,
                    cancellationToken: AbortToken
                )
            );
        }
        catch (PostgresException)
        {
            // Schema may not exist if test failed before initialization
        }

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
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.ConnectionString);
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

        _postgreSqlOptions = provider.GetRequiredService<IOptions<PostgreSqlOptions>>();
        _messagingOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();
        _serializer = provider.GetRequiredService<ISerializer>();

        _initializer = new PostgreSqlStorageInitializer(
            NullLogger<PostgreSqlStorageInitializer>.Instance,
            _postgreSqlOptions,
            _messagingOptions
        );

        _storage = _CreateStorage(TimeProvider.System);
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
    public override Task should_use_database_clock_when_reclaiming_published_retry_lease() =>
        base.should_use_database_clock_when_reclaiming_published_retry_lease();

    [Fact]
    public override Task should_use_database_clock_when_reclaiming_received_retry_lease() =>
        base.should_use_database_clock_when_reclaiming_received_retry_lease();

    [Fact]
    public override Task should_use_database_clock_when_fast_forwarding_dead_owner_lease() =>
        base.should_use_database_clock_when_fast_forwarding_dead_owner_lease();

    [Fact]
    public override Task should_stamp_retry_lease_from_database_clock() =>
        base.should_stamp_retry_lease_from_database_clock();

    [Fact]
    public override Task should_use_application_clock_when_scheduling_published_retry() =>
        base.should_use_application_clock_when_scheduling_published_retry();

    [Fact]
    public override Task should_use_application_clock_when_scheduling_received_retry() =>
        base.should_use_application_clock_when_scheduling_received_retry();

    [Fact]
    public override Task should_reject_mismatched_original_retries() =>
        base.should_reject_mismatched_original_retries();

    [Fact]
    public override Task should_lease_and_reserve_publish_attempt_in_single_step() =>
        base.should_lease_and_reserve_publish_attempt_in_single_step();

    [Fact]
    public override Task should_reject_lease_and_reserve_with_stale_inline_attempts_token() =>
        base.should_reject_lease_and_reserve_with_stale_inline_attempts_token();

    [Fact]
    public override Task should_reject_stale_published_lease_generation_writes() =>
        base.should_reject_stale_published_lease_generation_writes();

    [Fact]
    public override Task should_reject_stale_received_lease_generation_writes() =>
        base.should_reject_stale_received_lease_generation_writes();

    [Fact]
    public override Task should_allow_published_fenced_writes_with_fast_application_clock() =>
        base.should_allow_published_fenced_writes_with_fast_application_clock();

    [Fact]
    public override Task should_allow_received_fenced_writes_with_fast_application_clock() =>
        base.should_allow_received_fenced_writes_with_fast_application_clock();

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

    #region PostgreSQL-Specific Tests

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public override Task should_stamp_fresh_dispatch_lease_from_database_clock(bool published, bool reserveAttempt) =>
        base.should_stamp_fresh_dispatch_lease_from_database_clock(published, reserveAttempt);

    [Fact]
    public async Task should_preserve_sub_second_fresh_dispatch_lease_duration()
    {
        var storage = GetStorage();
        var message = await storage.StoreMessageAsync(
            "sub-second-database-clock-lease",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        var leaseDuration = TimeSpan.FromMilliseconds(750);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var databaseTimeBefore = await connection.ExecuteScalarAsync<DateTime>("SELECT statement_timestamp()");

        (await storage.LeasePublishAsync(message, leaseDuration, AbortToken)).Should().BeTrue();

        var persistedLease = await connection.QuerySingleAsync<PersistedLease>(
            """SELECT statement_timestamp() AS "DatabaseTimeAfter", "LockedUntil", "Owner" FROM messaging.published WHERE "Id"=@Id""",
            new { Id = message.StorageId }
        );
        persistedLease.LockedUntil.Should().BeOnOrAfter(databaseTimeBefore.Add(leaseDuration));
        persistedLease.LockedUntil.Should().BeOnOrBefore(persistedLease.DatabaseTimeAfter.Add(leaseDuration));
    }

    [Fact]
    public async Task should_preserve_active_receive_lease_when_fast_application_clock_redelivers()
    {
        var storage = GetStorage();
        var origin = CreateMessage();
        var stored = await storage.StoreReceivedMessageAsync(
            "fast-clock-redelivery",
            "fast-clock-group",
            origin,
            AbortToken
        );
        await storage.ChangeReceiveStateAsync(
            stored,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );

        NodeMembership.SetIdentity("fast-clock-redelivery-owner");
        (await storage.LeaseReceiveAsync(stored, TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var before = await connection.QuerySingleAsync<PersistedLease>(
            """SELECT statement_timestamp() AS "DatabaseTimeAfter", "LockedUntil", "Owner" FROM messaging.received WHERE "Id"=@Id""",
            new { Id = stored.StorageId }
        );

        var fastClockStorage = _CreateStorage(
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddYears(10))
        );
        var redelivery = await fastClockStorage.StoreReceivedMessageAsync(
            "fast-clock-redelivery",
            "fast-clock-group",
            origin,
            AbortToken
        );
        var after = await connection.QuerySingleAsync<PersistedLease>(
            """SELECT statement_timestamp() AS "DatabaseTimeAfter", "LockedUntil", "Owner" FROM messaging.received WHERE "Id"=@Id""",
            new { Id = stored.StorageId }
        );

        redelivery.StorageId.Should().NotBe(stored.StorageId, "the active-lease guard must reject the upsert");
        after.LockedUntil.Should().Be(before.LockedUntil);
        after.Owner.Should().Be(before.Owner);
    }

    [Fact]
    public async Task should_create_database_schema()
    {
        // given, when
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // then
        var result = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'messaging'"
        );
        result.Should().Be("messaging");
    }

    [Theory]
    [InlineData("messaging.published")]
    [InlineData("messaging.received")]
    public async Task should_create_tables(string tableName)
    {
        // given, when
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var parts = tableName.Split('.');
        var schema = parts[0];
        var table = parts[1];

        // then
        var result = await connection.QueryFirstOrDefaultAsync<string>(
            $"""
            SELECT table_name FROM information_schema.tables
            WHERE table_catalog='messages_test' AND table_schema = '{schema}' AND table_name = '{table}'
            """
        );
        result.Should().Be(table);
    }

    [Fact]
    public async Task should_return_postgresql_monitoring_api()
    {
        // given
        var storage = GetStorage();

        // when
        var monitoringApi = storage.GetMonitoringApi();

        // then
        monitoringApi.Should().BeOfType<PostgreSqlMonitoringApi>();
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("published")]
    [InlineData("received")]
    public async Task should_create_status_name_added_composite_index(string table)
    {
        // #508 — the initializer creates the final ("StatusName","Added") dashboard index directly.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var indexDef = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'messaging' AND indexname = @IndexName",
            new { IndexName = $"idx_{table}_StatusName_Added" }
        );

        indexDef.Should().NotBeNull().And.Contain("\"StatusName\", \"Added\"");
    }

    [Fact]
    public async Task should_create_queued_partial_index_on_published()
    {
        // #509 — partial index for the Queued branch of the delayed scheduler's OR predicate.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var indexDef = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'messaging' AND indexname = 'idx_published_Version_ExpiresAt_Queued'"
        );

        indexDef.Should().NotBeNull();
        indexDef.Should().Contain("\"Version\", \"ExpiresAt\"");
        indexDef.Should().Contain("WHERE").And.Contain("Queued");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("received")]
    public async Task should_create_content_trgm_gin_index_when_pg_trgm_available(string table)
    {
        // #507 — the container role can CREATE EXTENSION pg_trgm, so the trigram content indexes are
        // created (happy path). This also proves moving CREATE EXTENSION out of the transaction did not
        // regress trigram-index creation. Managed-PG graceful degradation is covered by the try/catch +
        // pg_extension probe in _TryEnsureTrgmExtensionAsync (needs a privilege-restricted role to exercise).
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var indexDef = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'messaging' AND indexname = @IndexName",
            new { IndexName = $"idx_{table}_Content_trgm" }
        );

        indexDef.Should().NotBeNull();
        indexDef.Should().Contain("gin").And.Contain("gin_trgm_ops");
    }

    [Fact]
    public async Task should_initialize_core_schema_when_restricted_role_cannot_create_pg_trgm()
    {
        await RestrictedPostgreSqlDatabase.ExecuteAsync(
            fixture.ConnectionString,
            preinstallTrgm: false,
            async connectionString =>
            {
                var initializer = _CreateInitializer(connectionString);

                await initializer.InitializeAsync(AbortToken);

                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(AbortToken);
                var tables = await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(1) FROM information_schema.tables WHERE table_schema = 'messaging' AND table_name IN ('published', 'received')",
                        cancellationToken: AbortToken
                    )
                );
                var coreIndexes = await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(1) FROM pg_indexes WHERE schemaname = 'messaging' AND indexname IN ('idx_published_StatusName_Added', 'idx_received_StatusName_Added')",
                        cancellationToken: AbortToken
                    )
                );
                var trgmIndexes = await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(1) FROM pg_indexes WHERE schemaname = 'messaging' AND indexname LIKE 'idx_%_Content_trgm'",
                        cancellationToken: AbortToken
                    )
                );

                tables.Should().Be(2);
                coreIndexes.Should().Be(2);
                trgmIndexes.Should().Be(0);
            },
            AbortToken
        );
    }

    [Fact]
    public async Task should_create_trgm_indexes_for_restricted_role_when_extension_is_preinstalled()
    {
        await RestrictedPostgreSqlDatabase.ExecuteAsync(
            fixture.ConnectionString,
            preinstallTrgm: true,
            async connectionString =>
            {
                var initializer = _CreateInitializer(connectionString);

                await initializer.InitializeAsync(AbortToken);

                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(AbortToken);
                var trgmIndexes = await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(1) FROM pg_indexes WHERE schemaname = 'messaging' AND indexname IN ('idx_published_Content_trgm', 'idx_received_Content_trgm')",
                        cancellationToken: AbortToken
                    )
                );

                trgmIndexes.Should().Be(2);
            },
            AbortToken
        );
    }

    [Theory]
    [InlineData("published")]
    [InlineData("received")]
    public async Task should_terminalize_poison_retry_row_when_content_cannot_deserialize(string tableName)
    {
        // given
        var storage = GetStorage();
        var id = Guid.NewGuid();
        var now = TimeProvider.GetUtcNow();

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await _InsertPoisonRetryRowAsync(connection, tableName, id, now);
        }

        // when
        var picked = string.Equals(tableName, "published", StringComparison.Ordinal)
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);

        // then
        picked.Should().NotContain(message => message.StorageId == id);

        await using var assertConnection = new NpgsqlConnection(fixture.ConnectionString);
        await assertConnection.OpenAsync(AbortToken);

        var statusName = await assertConnection.ExecuteScalarAsync<string>(
            $"""SELECT "StatusName" FROM messaging.{tableName} WHERE "Id" = @Id""",
            new { Id = id }
        );
        var nextRetryAt = await assertConnection.ExecuteScalarAsync<DateTimeOffset?>(
            $"""SELECT "NextRetryAt" FROM messaging.{tableName} WHERE "Id" = @Id""",
            new { Id = id }
        );
        var lockedUntil = await assertConnection.ExecuteScalarAsync<DateTimeOffset?>(
            $"""SELECT "LockedUntil" FROM messaging.{tableName} WHERE "Id" = @Id""",
            new { Id = id }
        );
        var owner = await assertConnection.ExecuteScalarAsync<string?>(
            $"""SELECT "Owner" FROM messaging.{tableName} WHERE "Id" = @Id""",
            new { Id = id }
        );

        statusName.Should().Be(nameof(StatusName.Failed));
        nextRetryAt.Should().BeNull();
        lockedUntil.Should().BeNull();
        owner.Should().BeNull();

        if (string.Equals(tableName, "received", StringComparison.Ordinal))
        {
            var exceptionInfo = await assertConnection.ExecuteScalarAsync<string?>(
                """SELECT "ExceptionInfo" FROM messaging.received WHERE "Id" = @Id""",
                new { Id = id }
            );
            exceptionInfo.Should().Contain("JsonException");
        }
    }

    [Theory]
    [InlineData("published")]
    [InlineData("received")]
    public async Task should_return_healthy_retry_row_when_same_claim_batch_contains_poison(string tableName)
    {
        var storage = GetStorage();
        var serializer = GetSerializer();
        var poisonId = Guid.NewGuid();
        var healthyId = Guid.NewGuid();
        var now = TimeProvider.GetUtcNow();

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
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

        var picked = string.Equals(tableName, "published", StringComparison.Ordinal)
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);

        picked.Select(message => message.StorageId).Should().Contain(healthyId).And.NotContain(poisonId);

        await using var assertConnection = new NpgsqlConnection(fixture.ConnectionString);
        await assertConnection.OpenAsync(AbortToken);
        var poisonNextRetryAt = await assertConnection.ExecuteScalarAsync<DateTimeOffset?>(
            $"""SELECT "NextRetryAt" FROM messaging.{tableName} WHERE "Id" = @Id""",
            new { Id = poisonId }
        );
        poisonNextRetryAt.Should().BeNull();
    }

    [Theory]
    [InlineData("published", "Added")]
    [InlineData("published", "ExpiresAt")]
    [InlineData("published", "NextRetryAt")]
    [InlineData("received", "Added")]
    [InlineData("received", "ExpiresAt")]
    [InlineData("received", "NextRetryAt")]
    public async Task should_use_timestamptz_for_time_columns(string table, string column)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var dataType = await connection.QueryFirstOrDefaultAsync<string>(
            $"""
            SELECT data_type FROM information_schema.columns
            WHERE table_schema = 'messaging' AND table_name = '{table}' AND column_name = '{column}'
            """
        );
        dataType.Should().Be("timestamp with time zone");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("received")]
    public async Task should_create_owner_column_with_shared_width(string table)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var dataType = await connection.QueryFirstOrDefaultAsync<string>(
            """
            SELECT data_type FROM information_schema.columns
            WHERE table_schema = 'messaging' AND table_name = @Table AND column_name = 'Owner'
            """,
            new { Table = table }
        );
        var maxLength = await connection.QueryFirstOrDefaultAsync<int?>(
            """
            SELECT character_maximum_length FROM information_schema.columns
            WHERE table_schema = 'messaging' AND table_name = @Table AND column_name = 'Owner'
            """,
            new { Table = table }
        );

        dataType.Should().Be("character varying");
        maxLength.Should().Be(DataStorageConstants.OwnerColumnMaxLength);
    }

    // -------------------------------------------------------------------------
    // EXPLAIN ANALYZE — verify the retry-pickup query plans use the partial
    // index (idx_*_next_retry). A regression to a full sequential scan would
    // silently degrade throughput at high message volumes; pinning the plan
    // in tests fails fast on accidental schema/query drift.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("idx_received_Version_NextRetryAt")]
    [InlineData("idx_published_Version_NextRetryAt")]
    public async Task should_key_retry_pickup_index_on_version_then_next_retry_at(string indexName)
    {
        // Pin the retry-pickup index shape: Version must be the leading key column so it is a
        // seek predicate, not a residual filter. Regression here would silently fan the planner
        // out to both versions during a rolling upgrade and discard rows post-fetch.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var columns = (
            await connection.QueryAsync<string>(
                """
                SELECT a.attname
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN unnest(i.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
                JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                WHERE n.nspname = 'messaging'
                  AND c.relname = @IndexName
                  AND k.ord <= i.indnkeyatts
                ORDER BY k.ord;
                """,
                new { IndexName = indexName }
            )
        ).ToList();

        columns.Should().BeEquivalentTo(["Version", "NextRetryAt"], opts => opts.WithStrictOrdering());

        // Filtered predicate must be `NextRetryAt IS NOT NULL` so terminal rows are physically
        // excluded from the index — keeps it small even under high failed-message volume.
        var predicate = await connection.QueryFirstOrDefaultAsync<string>(
            """
            SELECT pg_get_expr(i.indpred, i.indrelid, true)
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indexrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'messaging' AND c.relname = @IndexName;
            """,
            new { IndexName = indexName }
        );
        predicate.Should().NotBeNull().And.Contain("NextRetryAt").And.Contain("IS NOT NULL");
    }

    [Theory]
    [InlineData("idx_received_Owner_not_null")]
    [InlineData("idx_published_Owner_not_null")]
    public async Task should_key_owner_index_on_owner_with_not_null_filter(string indexName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var columns = (
            await connection.QueryAsync<string>(
                """
                SELECT a.attname
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN unnest(i.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
                JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                WHERE n.nspname = 'messaging'
                  AND c.relname = @IndexName
                  AND k.ord <= i.indnkeyatts
                ORDER BY k.ord;
                """,
                new { IndexName = indexName }
            )
        ).ToList();

        columns.Should().BeEquivalentTo(["Owner"], opts => opts.WithStrictOrdering());

        var predicate = await connection.QueryFirstOrDefaultAsync<string>(
            """
            SELECT pg_get_expr(i.indpred, i.indrelid, true)
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indexrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'messaging' AND c.relname = @IndexName;
            """,
            new { IndexName = indexName }
        );

        predicate.Should().NotBeNull().And.Contain("Owner").And.Contain("IS NOT NULL");
    }

    [Theory]
    [InlineData("received", "idx_received_Version_NextRetryAt")]
    [InlineData("published", "idx_published_Version_NextRetryAt")]
    public async Task should_use_partial_indexes_in_retry_pickup_query_plan(string tableSuffix, string nextRetryIndex)
    {
        // given — the planner only prefers an index over a sequential scan once the table has
        // enough rows for an index lookup to be cheaper. A freshly-initialised test container
        // starts with an empty table, so we synthesise a small load profile (a mix of rows that
        // satisfy and violate the partial-index predicate) and ANALYZE to refresh statistics.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var qualifiedTable = $"messaging.\"{tableSuffix}\"";
        const int seedRows = 50;
        var seedSql = tableSuffix switch
        {
            "received" => $$"""
                INSERT INTO {{qualifiedTable}} ("Id","Version","Name","Group","Content","IntentType","Retries","Added","ExpiresAt","NextRetryAt","LockedUntil","StatusName","MessageId")
                SELECT gen_random_uuid(), 'v1', 'plan-test', NULL, '{}', 0, 0, now(), NULL,
                       CASE WHEN g % 2 = 0 THEN now() - interval '1 minute' ELSE NULL END,
                       NULL, 'Failed', 'plan-' || g
                FROM generate_series(1000, 1000 + {{seedRows - 1}}) g
                ON CONFLICT DO NOTHING;
                """,
            "published" => $$"""
                INSERT INTO {{qualifiedTable}} ("Id","Version","Name","Content","IntentType","Retries","Added","ExpiresAt","NextRetryAt","LockedUntil","StatusName","MessageId")
                SELECT gen_random_uuid(), 'v1', 'plan-test', '{}', 0, 0, now(), NULL,
                       CASE WHEN g % 2 = 0 THEN now() - interval '1 minute' ELSE NULL END,
                       NULL, 'Failed', 'plan-' || g
                FROM generate_series(1000, 1000 + {{seedRows - 1}}) g
                ON CONFLICT DO NOTHING;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(tableSuffix), tableSuffix, "Unknown table suffix."),
        };
        await connection.ExecuteAsync(seedSql);
        await connection.ExecuteAsync($"ANALYZE {qualifiedTable};");

        // The query mirrors PostgreSqlDataStorage._GetMessagesOfNeedRetryAsync. We omit
        // "FOR UPDATE SKIP LOCKED" because EXPLAIN does not require row-level locking and
        // the planner does not include it in the index-selection decision under FORMAT JSON.
        //
        // Disable seq scan for the plan capture so the test pins the planner's preferred
        // *index*-backed path. With seq scan enabled, PostgreSQL can still pick the partial
        // index, but on small tables it may pick a seq scan even after seeding — the assertion
        // we care about is "the planner has a usable index", not "the planner picks it for 50
        // rows".
        var explainSql = $"""
            SET LOCAL enable_seqscan = off;
            EXPLAIN (ANALYZE, FORMAT JSON)
            SELECT "Id","Content","IntentType","Retries","Added","NextRetryAt" FROM {qualifiedTable}
            WHERE "Retries" <= @Retries
              AND "Version" = @Version
              AND "NextRetryAt" IS NOT NULL
              AND "NextRetryAt" <= now()
            LIMIT 200
            """;

        // when
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        var planJson = await connection.QueryFirstOrDefaultAsync<string>(
            explainSql,
            new { Retries = 50, Version = "v1" },
            transaction: transaction
        );
        await transaction.CommitAsync(AbortToken);

        // then — the plan should reference the partial index.
        planJson.Should().NotBeNullOrEmpty();
        var nodeTypes = _CollectNodeTypes(planJson!);
        var indexNames = _CollectIndexNames(planJson!);

        var indexScanNodeTypes = new[] { "Index Scan", "Index Only Scan", "Bitmap Index Scan", "Bitmap Heap Scan" };
        nodeTypes.Should().Contain(t => indexScanNodeTypes.Contains(t));
        indexNames
            .Should()
            .Contain(
                name => string.Equals(name, nextRetryIndex, StringComparison.Ordinal),
                because: "the retry-pickup query must use the partial index ({0}) to avoid sequential scans",
                nextRetryIndex
            );
    }

    private PostgreSqlStorageInitializer _CreateInitializer(string connectionString)
    {
        return new PostgreSqlStorageInitializer(
            NullLogger<PostgreSqlStorageInitializer>.Instance,
            Options.Create(new PostgreSqlOptions { ConnectionString = connectionString }),
            Options.Create(new MessagingOptions())
        );
    }

    private static List<string> _CollectNodeTypes(string explainJson)
    {
        var nodeTypes = new List<string>();
        using var doc = JsonDocument.Parse(explainJson);
        foreach (var planEntry in doc.RootElement.EnumerateArray())
        {
            if (planEntry.TryGetProperty("Plan", out var rootPlan))
            {
                _WalkPlanForProperty(rootPlan, "Node Type", nodeTypes);
            }
        }

        return nodeTypes;
    }

    private static List<string> _CollectIndexNames(string explainJson)
    {
        var indexNames = new List<string>();
        using var doc = JsonDocument.Parse(explainJson);
        foreach (var planEntry in doc.RootElement.EnumerateArray())
        {
            if (planEntry.TryGetProperty("Plan", out var rootPlan))
            {
                _WalkPlanForProperty(rootPlan, "Index Name", indexNames);
            }
        }

        return indexNames;
    }

    private static async Task _InsertPoisonRetryRowAsync(
        NpgsqlConnection connection,
        string tableName,
        Guid id,
        DateTimeOffset now
    )
    {
        if (string.Equals(tableName, "published", StringComparison.Ordinal))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO messaging.published
                    ("Id", "Version", "Name", "Content", "IntentType", "Retries", "Added", "ExpiresAt", "NextRetryAt", "LockedUntil", "Owner", "StatusName", "MessageId")
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
            INSERT INTO messaging.received
                ("Id", "Version", "Name", "Group", "Content", "IntentType", "Retries", "Added", "ExpiresAt", "NextRetryAt", "LockedUntil", "Owner", "StatusName", "MessageId", "ExceptionInfo")
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

    private static Task _InsertHealthyRetryRowAsync(
        NpgsqlConnection connection,
        string tableName,
        Guid id,
        string content,
        DateTimeOffset now
    )
    {
        if (string.Equals(tableName, "published", StringComparison.Ordinal))
        {
            return connection.ExecuteAsync(
                """
                INSERT INTO messaging.published
                    ("Id", "Version", "Name", "Content", "IntentType", "Retries", "Added", "ExpiresAt", "NextRetryAt", "LockedUntil", "Owner", "StatusName", "MessageId")
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
            INSERT INTO messaging.received
                ("Id", "Version", "Name", "Group", "Content", "IntentType", "Retries", "Added", "ExpiresAt", "NextRetryAt", "LockedUntil", "Owner", "StatusName", "MessageId", "ExceptionInfo")
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

    private static void _WalkPlanForProperty(JsonElement plan, string propertyName, List<string> collector)
    {
        if (plan.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString();
            if (!string.IsNullOrEmpty(str))
            {
                collector.Add(str);
            }
        }

        if (plan.TryGetProperty("Plans", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                _WalkPlanForProperty(child, propertyName, collector);
            }
        }
    }

    private sealed class PersistedLease
    {
        public required DateTime DatabaseTimeAfter { get; init; }

        public required DateTime LockedUntil { get; init; }

        public string? Owner { get; init; }
    }

    #endregion
}
