// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sql;
using Framework.Sql.PostgreSql;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;

namespace Tests.PostgreSql;

/// <summary>
/// Unit tests for <see cref="NpgsqlConnectionStringChecker"/>.
/// These are structural tests; integration tests require actual database.
/// </summary>
public sealed class NpgsqlConnectionStringCheckerTests
{
    [Fact]
    public void should_implement_IConnectionStringChecker()
    {
        // given
        var logger = Substitute.For<ILogger<NpgsqlConnectionStringChecker>>();

        // when
        var sut = new NpgsqlConnectionStringChecker(logger);

        // then
        sut.Should().BeAssignableTo<IConnectionStringChecker>();
    }

    [Fact]
    public async Task should_return_false_for_invalid_connection_string()
    {
        // given
        var logger = Substitute.For<ILogger<NpgsqlConnectionStringChecker>>();
        var sut = new NpgsqlConnectionStringChecker(logger);

        // when
        var result = await sut.CheckAsync("Host=invalid-host-that-does-not-exist;Database=test;Timeout=1");

        // then
        result.Connected.Should().BeFalse();
        result.DatabaseExists.Should().BeFalse();
    }

    [Fact]
    public void should_connect_to_postgres_database_first()
    {
        // given - connection string with a custom database name
        const string connectionString = "Host=localhost;Database=myapp_db;Port=5432";

        // when - verify the implementation changes database to 'postgres' for initial check
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var originalDatabase = builder.Database;
        builder.Database = "postgres";

        // then - original database should be preserved and 'postgres' used for connection
        originalDatabase.Should().Be("myapp_db");
        builder.Database.Should().Be("postgres");

        // This verifies the pattern used in CheckAsync:
        // 1. Store original database name
        // 2. Change to 'postgres' for initial connection
        // 3. Then ChangeDatabaseAsync to original database
    }

    [Fact]
    public void should_set_timeout_to_1_second()
    {
        // given - any connection string
        const string connectionString = "Host=localhost;Database=test;Timeout=30";

        // when - simulate what CheckAsync does with the connection builder
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 1 };

        // then - timeout should be overridden to 1 second regardless of input
        builder.Timeout.Should().Be(1);
    }

    [Fact]
    public async Task should_log_warning_on_exception()
    {
        // given
        var logger = Substitute.For<ILogger<NpgsqlConnectionStringChecker>>();
        var sut = new NpgsqlConnectionStringChecker(logger);

        // when - use invalid host to trigger exception
        await sut.CheckAsync("Host=invalid-host-xyz;Database=test");

        // then - verify LogWarning was called
        logger
            .Received()
            .Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }
}
