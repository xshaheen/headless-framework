using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Framework.Sql;

namespace Tests;

public abstract class SqlConnectionFactoryTestBase
{
    public abstract string GetConnection();

    public abstract ISqlConnectionFactory GetFactory();

    public virtual async Task should_return_connection_string()
    {
        // given
        await using var sut = GetFactory();

        // when
        var result = sut.GetConnectionString();

        // then
        result.Should().Be(GetConnection());
    }

    public virtual async Task should_create_new_connection()
    {
        // given
        await using var sut = GetFactory();

        // when
        var connection = await sut.CreateNewConnectionAsync();

        // then
        connection.Should().NotBeNull();
        connection.State.Should().Be(ConnectionState.Open);
    }

    public virtual async Task should_get_open_connection()
    {
        // given
        await using var sut = GetFactory();

        // when
        var connection = await sut.GetOpenConnectionAsync();

        // then
        connection.Should().NotBeNull();
        connection.State.Should().Be(ConnectionState.Open);
    }

    public virtual async Task should_dispose_connection()
    {
        // given
        var sut = GetFactory();
        var connection = await sut.GetOpenConnectionAsync();

        // when
        await sut.DisposeAsync();

        // then
        connection.State.Should().Be(ConnectionState.Closed);
    }

    public virtual async Task should_get_open_connection_concurrently()
    {
        // given
        await using var sut = GetFactory();
        using var connections = new BlockingCollection<DbConnection>();

        // when
        await Parallel.ForEachAsync(
            Enumerable.Range(1, 10),
            async (_, token) =>
            {
                var connection = await sut.GetOpenConnectionAsync();
                connections.Add(connection, token);
            }
        );

        // then
        connections.Should().HaveCount(10);
        connections.Should().NotContainNulls();
        connections.Should().OnlyContain(connection => connection.State == ConnectionState.Open);

        // All the connections items is the same instance
        for (var i = 0; i < connections.Count; i += 2)
        {
            connections.ElementAt(i).Should().BeSameAs(connections.ElementAt(i + 1));
        }
    }
}
