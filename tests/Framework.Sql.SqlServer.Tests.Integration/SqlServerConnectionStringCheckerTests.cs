// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sql.SqlServer;
using Framework.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Tests.TestSetup;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerConnectionStringCheckerTests(SqlServerTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_return_connected_true_when_server_reachable()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        var sut = new SqlServerConnectionStringChecker(logger);
        var connectionString = fixture.GetConnectionString();

        // when
        var (connected, _) = await sut.CheckAsync(connectionString);

        // then
        connected.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_database_exists_true_when_db_exists()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        var sut = new SqlServerConnectionStringChecker(logger);
        var connectionString = fixture.GetConnectionString(); // master DB exists by default

        // when
        var (_, databaseExists) = await sut.CheckAsync(connectionString);

        // then
        databaseExists.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_connected_false_when_server_unreachable()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        var sut = new SqlServerConnectionStringChecker(logger);
        var connectionString =
            "Server=192.0.2.1,1433;Database=test;User Id=sa;Password=pass;Connect Timeout=1;TrustServerCertificate=True";

        // when
        var (connected, _) = await sut.CheckAsync(connectionString);

        // then
        connected.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_database_exists_false_when_db_missing()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        var sut = new SqlServerConnectionStringChecker(logger);
        var builder = new SqlConnectionStringBuilder(fixture.GetConnectionString())
        {
            InitialCatalog = "NonExistentDatabase12345",
        };

        // when
        var (connected, databaseExists) = await sut.CheckAsync(builder.ConnectionString);

        // then
        connected.Should().BeTrue();
        databaseExists.Should().BeFalse();
    }

    [Fact]
    public async Task should_change_to_target_database()
    {
        // given - create a new database to test change database
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        var sut = new SqlServerConnectionStringChecker(logger);
        var testDbName = $"TestDb_{Guid.NewGuid():N}";

        await using var setupConnection = new SqlConnection(fixture.GetConnectionString());
        await setupConnection.OpenAsync(AbortToken);
        await using var createDbCommand = setupConnection.CreateCommand();
        createDbCommand.CommandText = $"CREATE DATABASE [{testDbName}]";
        await createDbCommand.ExecuteNonQueryAsync(AbortToken);

        try
        {
            var builder = new SqlConnectionStringBuilder(fixture.GetConnectionString()) { InitialCatalog = testDbName };

            // when
            var (connected, databaseExists) = await sut.CheckAsync(builder.ConnectionString);

            // then
            connected.Should().BeTrue();
            databaseExists.Should().BeTrue();
        }
        finally
        {
            // cleanup - set to single user mode and drop
            await using var dropDbCommand = setupConnection.CreateCommand();
            dropDbCommand.CommandText = $"""
                ALTER DATABASE [{testDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{testDbName}];
                """;
            await dropDbCommand.ExecuteNonQueryAsync(AbortToken);
        }
    }

    [Fact]
    public async Task should_close_connection_after_check()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        var sut = new SqlServerConnectionStringChecker(logger);
        var connectionString = fixture.GetConnectionString();

        // when
        await sut.CheckAsync(connectionString);

        // then - verify we can still connect (connection pool works, no leaked connections)
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task should_handle_malformed_connection_string()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        var sut = new SqlServerConnectionStringChecker(logger);
        const string malformedConnectionString = "this is not a valid connection string";

        // when
        var (connected, databaseExists) = await sut.CheckAsync(malformedConnectionString);

        // then
        connected.Should().BeFalse();
        databaseExists.Should().BeFalse();
    }
}
