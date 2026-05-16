// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Dapper;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Headless.Messaging.Serialization;
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
    private ILongIdGenerator? _longIdGenerator;
    private ISerializer? _serializer;

    /// <inheritdoc />
    protected override DataStorageCapabilities Capabilities =>
        new()
        {
            SupportsLocking = true,
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
                """
                TRUNCATE TABLE messaging.published;
                TRUNCATE TABLE messaging.received;
                UPDATE messaging.lock SET "Instance"='', "LastLockTime"='0001-01-01 00:00:00';
                """
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
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
        services.AddSingleton(TimeProvider.System);

        var provider = services.BuildServiceProvider();

        var postgreSqlOptions = provider.GetRequiredService<IOptions<PostgreSqlOptions>>();
        var messagingOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();

        _longIdGenerator = provider.GetRequiredService<ILongIdGenerator>();
        _serializer = provider.GetRequiredService<ISerializer>();

        _initializer = new PostgreSqlStorageInitializer(
            NullLogger<PostgreSqlStorageInitializer>.Instance,
            postgreSqlOptions,
            messagingOptions
        );

        _storage = new PostgreSqlDataStorage(
            postgreSqlOptions,
            messagingOptions,
            _initializer,
            provider.GetRequiredService<ISerializer>(),
            _longIdGenerator,
            TimeProvider.System
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
    public override Task should_get_published_messages_of_need_retry() =>
        base.should_get_published_messages_of_need_retry();

    [Fact]
    public override Task should_get_received_messages_of_need_retry() =>
        base.should_get_received_messages_of_need_retry();

    [Fact]
    public override Task should_acquire_lock() => base.should_acquire_lock();

    [Fact]
    public override Task should_not_acquire_lock_when_already_held() =>
        base.should_not_acquire_lock_when_already_held();

    [Fact]
    public override Task should_release_lock() => base.should_release_lock();

    [Fact]
    public override Task should_renew_lock() => base.should_renew_lock();

    [Fact]
    public override Task should_delete_expired_messages() => base.should_delete_expired_messages();

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
    public override Task should_pickup_message_at_max_persisted_retries_and_exclude_above() =>
        base.should_pickup_message_at_max_persisted_retries_and_exclude_above();

    [Fact]
    public override Task should_not_return_leased_received_message_until_lease_expires() =>
        base.should_not_return_leased_received_message_until_lease_expires();

    [Fact]
    public override Task should_handle_concurrent_state_updates_to_same_row() =>
        base.should_handle_concurrent_state_updates_to_same_row();

    #endregion

    #region PostgreSQL-Specific Tests

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
                WHERE n.nspname = 'messaging' AND c.relname = @IndexName
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
    [InlineData("received", "idx_received_Version_NextRetryAt")]
    [InlineData("published", "idx_published_Version_NextRetryAt")]
    public async Task should_use_partial_indexes_in_retry_pickup_query_plan(string tableSuffix, string nextRetryIndex)
    {
        // given — ensure the table has at least a few rows in each predicate branch so
        // the planner has a reason to choose the partial index over a seq scan on a tiny table.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // Force any cached plans to refresh against current statistics.
        await connection.ExecuteAsync($"ANALYZE messaging.{tableSuffix};");

        var qualifiedTable = $"messaging.\"{tableSuffix}\"";

        // The query mirrors PostgreSqlDataStorage._GetMessagesOfNeedRetryAsync. We omit
        // "FOR UPDATE SKIP LOCKED" because EXPLAIN does not require row-level locking and
        // the planner does not include it in the index-selection decision under FORMAT JSON.
        var explainSql = $"""
            EXPLAIN (ANALYZE, FORMAT JSON)
            SELECT "Id","Content","Retries","Added","NextRetryAt" FROM {qualifiedTable}
            WHERE "Retries" <= @Retries
              AND "Version" = @Version
              AND "NextRetryAt" IS NOT NULL
              AND "NextRetryAt" <= now()
            LIMIT 200
            """;

        // when
        var planJson = await connection.QueryFirstOrDefaultAsync<string>(
            explainSql,
            new { Retries = 50, Version = "v1" }
        );

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

    #endregion
}
