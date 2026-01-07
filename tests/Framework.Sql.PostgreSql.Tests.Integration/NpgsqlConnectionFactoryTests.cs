using Framework.Sql;
using Framework.Sql.PostgreSql;
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
        command.CommandText = "CREATE TABLE test (id INT PRIMARY KEY, Name TEXT)";
        await command.ExecuteNonQueryAsync(AbortToken);

        // when
        command.CommandText = "INSERT INTO test (Id, Name) VALUES (1, 'test')";
        await command.ExecuteNonQueryAsync(AbortToken);

        // then
        command.CommandText = "SELECT COUNT(*) FROM test";
        var result = await command.ExecuteScalarAsync(AbortToken);
        result.Should().Be(1);
    }
}
