// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.SqlServer;
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
    private ILongIdGenerator? _longIdGenerator;

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
        await connection.ExecuteAsync(
            "TRUNCATE TABLE messaging.Published; TRUNCATE TABLE messaging.Received; DELETE FROM messaging.Lock;"
        );

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
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
        services.AddSingleton(TimeProvider.System);

        var provider = services.BuildServiceProvider();

        var sqlServerOptions = provider.GetRequiredService<IOptions<SqlServerOptions>>();
        var messagingOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();

        _longIdGenerator = provider.GetRequiredService<ILongIdGenerator>();
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
    public override Task should_handle_concurrent_state_updates_to_same_row() =>
        base.should_handle_concurrent_state_updates_to_same_row();

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
    [InlineData("Lock")]
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

    #endregion
}
