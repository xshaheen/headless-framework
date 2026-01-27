using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Framework.Sql;
using Framework.Testing.Tests;

namespace Tests;

public abstract class SqlConnectionFactoryTestBase : TestBase
{
    public abstract string GetConnection();

    public abstract ISqlConnectionFactory GetFactory();

    public ISqlCurrentConnection GetCurrent() => new DefaultSqlCurrentConnection(GetFactory());

    public virtual Task should_return_connection_string()
    {
        // given
        var sut = GetFactory();

        // when
        var result = sut.GetConnectionString();

        // then
        result.Should().Be(GetConnection());

        return Task.CompletedTask;
    }

    public virtual async Task should_create_new_connection()
    {
        // given
        var sut = GetFactory();

        // when
        var connection = await sut.CreateNewConnectionAsync(AbortToken);

        // then
        connection.Should().NotBeNull();
        connection.State.Should().Be(ConnectionState.Open);
    }

    public virtual async Task should_get_open_connection()
    {
        // given
        var sut = GetCurrent();

        // when
        var connection = await sut.GetOpenConnectionAsync(AbortToken);

        // then
        connection.Should().NotBeNull();
        connection.State.Should().Be(ConnectionState.Open);
    }

    public virtual async Task should_dispose_connection()
    {
        // given
        var sut = GetCurrent();
        var connection = await sut.GetOpenConnectionAsync(AbortToken);

        // when
        await sut.DisposeAsync();

        // then
        connection.State.Should().Be(ConnectionState.Closed);
    }

    public virtual async Task should_get_open_connection_concurrently()
    {
        // given
        var sut = GetCurrent();
        using var connections = new BlockingCollection<DbConnection>();

        // when
        await Parallel.ForEachAsync(
            Enumerable.Range(1, 10),
            AbortToken,
            async (_, token) =>
            {
                var connection = await sut.GetOpenConnectionAsync(token);
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

    // Cross-provider common behavior tests

    /// <summary>
    /// Verifies all providers return connection in Open state.
    /// </summary>
    public virtual async Task should_return_open_connection_for_all_providers()
    {
        // given
        var factory = GetFactory();

        // when
        var connection = await factory.CreateNewConnectionAsync(AbortToken);

        // then
        connection.Should().NotBeNull();
        connection.State.Should().Be(ConnectionState.Open);
        await connection.DisposeAsync();
    }

    /// <summary>
    /// Verifies factory is thread-safe for concurrent connection creation.
    /// </summary>
    public virtual async Task should_support_concurrent_connection_creation()
    {
        // given
        var factory = GetFactory();
        var connections = new ConcurrentBag<DbConnection>();
        const int concurrentCount = 5;

        // when
        await Parallel.ForEachAsync(
            Enumerable.Range(1, concurrentCount),
            AbortToken,
            async (_, token) =>
            {
                var connection = await factory.CreateNewConnectionAsync(token);
                connections.Add(connection);
            }
        );

        // then
        connections.Should().HaveCount(concurrentCount);
        connections.Should().OnlyContain(c => c.State == ConnectionState.Open);

        // cleanup
        foreach (var conn in connections)
        {
            await conn.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies same connection is reused across multiple operations within same scope.
    /// </summary>
    public virtual async Task should_reuse_connection_across_operations()
    {
        // given
        await using var sut = GetCurrent();

        // when - multiple sequential accesses
        var first = await sut.GetOpenConnectionAsync(AbortToken);
        var second = await sut.GetOpenConnectionAsync(AbortToken);
        var third = await sut.GetOpenConnectionAsync(AbortToken);

        // then - all should be same instance
        first.Should().BeSameAs(second);
        second.Should().BeSameAs(third);
    }

    /// <summary>
    /// Verifies AsyncLock prevents race conditions with parallel access.
    /// </summary>
    public virtual async Task should_handle_parallel_access_correctly()
    {
        // given
        await using var sut = GetCurrent();
        var connections = new ConcurrentBag<DbConnection>();
        const int parallelCount = 10;

        // when - parallel access
        await Task.WhenAll(
            Enumerable
                .Range(0, parallelCount)
                .Select(async _ =>
                {
                    var conn = await sut.GetOpenConnectionAsync(AbortToken);
                    connections.Add(conn);
                })
        );

        // then - all return same connection, all open
        connections.Should().HaveCount(parallelCount);
        connections.Should().OnlyContain(c => c.State == ConnectionState.Open);
        connections.Distinct().Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies connection is recreated after manual close.
    /// </summary>
    public virtual async Task should_reconnect_after_connection_drop()
    {
        // given
        await using var sut = GetCurrent();
        var initialConnection = await sut.GetOpenConnectionAsync(AbortToken);

        // when - simulate connection drop by closing it
        await initialConnection.CloseAsync();
        var newConnection = await sut.GetOpenConnectionAsync(AbortToken);

        // then - should get new open connection
        newConnection.State.Should().Be(ConnectionState.Open);
        newConnection.Should().NotBeSameAs(initialConnection);
    }
}
