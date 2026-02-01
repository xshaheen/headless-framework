// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.Sqlite;

namespace Tests.Sqlite;

/// <summary>
/// Unit tests for <see cref="SqliteConnectionFactory"/>.
/// These are structural tests; integration tests require actual database.
/// </summary>
public sealed class SqliteConnectionFactoryTests
{
    [Fact]
    public void should_implement_ISqlConnectionFactory()
    {
        // given
        const string connectionString = "Data Source=:memory:";

        // when
        var sut = new SqliteConnectionFactory(connectionString);

        // then
        sut.Should().BeAssignableTo<ISqlConnectionFactory>();
    }

    [Fact]
    public void should_store_connection_string()
    {
        // given
        const string connectionString = "Data Source=test.db";

        // when
        var sut = new SqliteConnectionFactory(connectionString);

        // then - verify stored via GetConnectionString
        sut.GetConnectionString().Should().Be(connectionString);
    }

    [Fact]
    public void should_return_connection_string()
    {
        // given
        const string connectionString = "Data Source=:memory:";

        // when
        var sut = new SqliteConnectionFactory(connectionString);

        // then
        sut.GetConnectionString().Should().Be(connectionString);
    }

    [Fact]
    public async Task should_create_open_connection_for_in_memory_database()
    {
        // given
        var ct = TestContext.Current.CancellationToken;
        const string connectionString = "Data Source=:memory:";
        var sut = new SqliteConnectionFactory(connectionString);

        // when
        await using var connection = await sut.CreateNewConnectionAsync(ct);

        // then
        connection.Should().NotBeNull();
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task should_implement_interface_explicitly()
    {
        // given
        var ct = TestContext.Current.CancellationToken;
        const string connectionString = "Data Source=:memory:";
        ISqlConnectionFactory sut = new SqliteConnectionFactory(connectionString);

        // when - call via interface
        await using var connection = await sut.CreateNewConnectionAsync(ct);

        // then - should delegate to typed method and return open connection
        connection.Should().NotBeNull();
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }
}
