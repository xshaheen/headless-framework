// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
            x.FailedRetryCount = 5;
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

    #endregion
}
