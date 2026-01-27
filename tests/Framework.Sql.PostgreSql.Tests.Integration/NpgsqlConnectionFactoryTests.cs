using System.Data;
using Framework.Sql;
using Framework.Sql.PostgreSql;
using Npgsql;
using Tests.TestSetup;

namespace Tests;

[Collection<NpgsqlTestFixture>]
public sealed class NpgsqlConnectionFactoryTests(NpgsqlTestFixture fixture) : SqlConnectionFactoryTestBase
{
    public override string GetConnection()
    {
        return fixture.Container.GetConnectionString();
    }

    public override ISqlConnectionFactory GetFactory()
    {
        return new NpgsqlConnectionFactory(GetConnection());
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
        command.CommandText = "CREATE TABLE test_exec (id INT PRIMARY KEY, Name TEXT)";
        await command.ExecuteNonQueryAsync(AbortToken);

        // when
        command.CommandText = "INSERT INTO test_exec (Id, Name) VALUES (1, 'test')";
        await command.ExecuteNonQueryAsync(AbortToken);

        // then
        command.CommandText = "SELECT COUNT(*) FROM test_exec";
        var result = await command.ExecuteScalarAsync(AbortToken);
        result.Should().Be(1);
    }

    [Fact]
    public async Task should_create_npgsql_connection()
    {
        // given
        var sut = new NpgsqlConnectionFactory(GetConnection());

        // when
        var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // then
        connection.Should().BeOfType<NpgsqlConnection>();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task should_open_connection_automatically()
    {
        // given
        var sut = new NpgsqlConnectionFactory(GetConnection());

        // when
        var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // then
        connection.State.Should().Be(ConnectionState.Open);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task should_pass_cancellation_token()
    {
        // given
        var sut = new NpgsqlConnectionFactory(GetConnection());
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
        // given
        var sut = new NpgsqlConnectionFactory(
            "Host=localhost;Port=5432;Database=test;Username=invalid;Password=invalid;Timeout=1"
        );

        // when
        var act = async () => await sut.CreateNewConnectionAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public async Task should_throw_on_unreachable_server()
    {
        // given - use a non-routable IP to ensure timeout
        var sut = new NpgsqlConnectionFactory(
            "Host=10.255.255.1;Port=5432;Database=test;Username=test;Password=test;Timeout=1"
        );

        // when
        var act = async () => await sut.CreateNewConnectionAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public async Task should_execute_query_on_created_connection()
    {
        // given
        var sut = new NpgsqlConnectionFactory(GetConnection());
        var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // when
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var result = await command.ExecuteScalarAsync(AbortToken);

        // then
        result.Should().Be(1);
        await connection.DisposeAsync();
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
