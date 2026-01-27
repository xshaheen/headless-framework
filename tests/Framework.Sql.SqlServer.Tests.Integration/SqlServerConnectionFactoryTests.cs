using System.Data;
using Framework.Sql;
using Framework.Sql.SqlServer;
using Microsoft.Data.SqlClient;
using Tests.TestSetup;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerConnectionFactoryTests(SqlServerTestFixture fixture) : SqlConnectionFactoryTestBase
{
    public override string GetConnection()
    {
        return fixture.GetConnectionString();
    }

    public override ISqlConnectionFactory GetFactory()
    {
        return new SqlServerConnectionFactory(GetConnection());
    }

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

    [Fact]
    public async Task should_execute_sql_command()
    {
        // given
        await using var sut = GetCurrent();
        var connection = await sut.GetOpenConnectionAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE test (id INT PRIMARY KEY, Name NVARCHAR(50))";
        await command.ExecuteNonQueryAsync(AbortToken);

        // when
        command.CommandText = "INSERT INTO test (Id, Name) VALUES (1, 'test')";
        await command.ExecuteNonQueryAsync(AbortToken);

        // then
        command.CommandText = "SELECT COUNT(*) FROM test";
        var result = await command.ExecuteScalarAsync(AbortToken);
        result.Should().Be(1);
    }

    [Fact]
    public async Task should_create_sql_connection()
    {
        // given
        var sut = GetFactory();

        // when
        await using var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // then
        connection.Should().BeOfType<SqlConnection>();
    }

    [Fact]
    public async Task should_open_connection_automatically()
    {
        // given
        var sut = GetFactory();

        // when
        await using var connection = await sut.CreateNewConnectionAsync(AbortToken);

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
        var act = async () => await sut.CreateNewConnectionAsync(cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_on_invalid_connection_string()
    {
        // given - malformed connection string
        var sut = new SqlServerConnectionFactory("invalid-connection-string");

        // when
        var act = async () => await sut.CreateNewConnectionAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_on_unreachable_server()
    {
        // given - unreachable server with short timeout
        var connectionString =
            "Server=192.0.2.1,1433;Database=test;User Id=sa;Password=pass;Connect Timeout=1;TrustServerCertificate=True";
        var sut = new SqlServerConnectionFactory(connectionString);

        // when
        var act = async () => await sut.CreateNewConnectionAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<SqlException>();
    }

    [Fact]
    public async Task should_execute_query_on_created_connection()
    {
        // given
        var sut = GetFactory();
        await using var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // when
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS TestValue";
        var result = await command.ExecuteScalarAsync(AbortToken);

        // then
        result.Should().Be(1);
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
