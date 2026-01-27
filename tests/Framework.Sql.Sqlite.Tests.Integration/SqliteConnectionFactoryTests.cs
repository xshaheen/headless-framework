// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Framework.Sql;
using Framework.Sql.Sqlite;
using Microsoft.Data.Sqlite;

namespace Tests;

public sealed class SqliteConnectionFactoryTests : SqlConnectionFactoryTestBase
{
    public override string GetConnection()
    {
        return "Data Source=:memory:";
    }

    public override ISqlConnectionFactory GetFactory()
    {
        return new SqliteConnectionFactory(GetConnection());
    }

    // Base class tests

    [Fact]
    public override Task should_return_connection_string()
    {
        return base.should_return_connection_string();
    }

    [Fact]
    public override Task should_create_new_connection()
    {
        return base.should_create_new_connection();
    }

    [Fact]
    public override Task should_get_open_connection()
    {
        return base.should_get_open_connection();
    }

    [Fact]
    public override Task should_dispose_connection()
    {
        return base.should_dispose_connection();
    }

    [Fact]
    public override Task should_get_open_connection_concurrently()
    {
        return base.should_get_open_connection_concurrently();
    }

    // SqliteConnectionFactory-specific tests (tests 55-56, 58-61)

    [Fact]
    public async Task should_create_sqlite_connection()
    {
        // given
        var sut = GetFactory();

        // when
        var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // then
        connection.Should().BeOfType<SqliteConnection>();
    }

    [Fact]
    public async Task should_open_connection_automatically()
    {
        // given
        var sut = GetFactory();

        // when
        var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // then
        connection.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async Task should_pass_cancellation_token()
    {
        // given
        var sut = GetFactory();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        Func<Task> act = async () => await sut.CreateNewConnectionAsync(cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_create_in_memory_database()
    {
        // given
        var sut = new SqliteConnectionFactory("Data Source=:memory:");

        // when
        var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // then
        connection.Should().NotBeNull();
        connection.State.Should().Be(ConnectionState.Open);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task should_create_file_based_database()
    {
        // given
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sqlite");
        var sut = new SqliteConnectionFactory($"Data Source={tempFile}");

        try
        {
            // when
            await using var connection = await sut.CreateNewConnectionAsync(AbortToken);

            // then
            connection.Should().NotBeNull();
            connection.State.Should().Be(ConnectionState.Open);
            File.Exists(tempFile).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task should_execute_query_on_created_connection()
    {
        // given
        var sut = GetFactory();
        await using var connection = await sut.CreateNewConnectionAsync(AbortToken);
        await using var command = connection.CreateCommand();

        // when
        command.CommandText = "SELECT sqlite_version()";
        var result = await command.ExecuteScalarAsync(AbortToken);

        // then
        result.Should().NotBeNull();
        result.Should().BeOfType<string>();
        ((string)result!).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task should_execute_sql_command()
    {
        // given
        await using var sut = GetCurrent();
        var connection = await sut.GetOpenConnectionAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)";
        await command.ExecuteNonQueryAsync(AbortToken);

        // when
        command.CommandText = "INSERT INTO test (name) VALUES ('test')";
        await command.ExecuteNonQueryAsync(AbortToken);

        // then
        command.CommandText = "SELECT COUNT(*) FROM test";
        var result = await command.ExecuteScalarAsync(AbortToken);
        result.Should().Be(1L);
    }

    // Cross-provider common behavior tests (tests 68-73)

    [Fact]
    public override Task should_return_open_connection_for_all_providers()
    {
        return base.should_return_open_connection_for_all_providers();
    }

    [Fact]
    public override Task should_support_concurrent_connection_creation()
    {
        return base.should_support_concurrent_connection_creation();
    }

    [Fact]
    public override Task should_reuse_connection_across_operations()
    {
        return base.should_reuse_connection_across_operations();
    }

    [Fact]
    public override Task should_handle_parallel_access_correctly()
    {
        return base.should_handle_parallel_access_correctly();
    }

    [Fact]
    public override Task should_reconnect_after_connection_drop()
    {
        return base.should_reconnect_after_connection_drop();
    }
}
