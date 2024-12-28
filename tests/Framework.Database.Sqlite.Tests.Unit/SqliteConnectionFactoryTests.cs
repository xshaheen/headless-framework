using System.Data;
using Framework.Database.Sqlite;
using Microsoft.Data.Sqlite;

namespace Tests;

public sealed class SqliteConnectionFactoryTests : IDisposable
{
    private const string _ConnectionString = "DataSource=:memory:";
    private readonly SqliteConnectionFactory _sut = new(_ConnectionString);

    [Fact]
    public void should_return_connection_string()
    {
        // when
        var result = _sut.GetConnectionString();

        // then
        result.Should().Be(_ConnectionString);
    }

    [Fact]
    public async Task should_create_new_connection()
    {
        // when
        var connection = await _sut.CreateNewConnectionAsync();

        // then
        connection.Should().NotBeNull();
        connection.Should().BeOfType<SqliteConnection>();
        connection.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async Task should_get_open_connection()
    {
        // when
        var connection = await _sut.GetOpenConnectionAsync();

        // then
        connection.Should().NotBeNull();
        connection.Should().BeOfType<SqliteConnection>();
        connection.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async Task should_dispose_connection()
    {
        // given
        var factory = new SqliteConnectionFactory(_ConnectionString);
        var connection = await factory.GetOpenConnectionAsync();

        // when
        await factory.DisposeAsync();

        // then
        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task should_execute_sql_command()
    {
        // given
        var connection = await _sut.GetOpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)";
        command.ExecuteNonQuery();

        // when
        command.CommandText = "INSERT INTO test (name) VALUES ('test')";
        command.ExecuteNonQuery();

        // then
        command.CommandText = "SELECT COUNT(*) FROM test";
        var result = command.ExecuteScalar();
        result.Should().Be(1);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
