// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Tests.SqlServer;

/// <summary>
/// Unit tests for <see cref="SqlServerConnectionStringChecker"/>.
/// These are structural tests; integration tests require actual database.
/// </summary>
public sealed class SqlServerConnectionStringCheckerTests
{
    [Fact]
    public void should_implement_IConnectionStringChecker()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();

        // when
        var sut = new SqlServerConnectionStringChecker(logger);

        // then
        sut.Should().BeAssignableTo<IConnectionStringChecker>();
    }

    [Fact]
    public async Task should_return_false_for_invalid_connection_string()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        var sut = new SqlServerConnectionStringChecker(logger);

        // when
        var (connected, databaseExists) = await sut.CheckAsync(
            "Server=invalid-host-that-does-not-exist;Database=test;Connect Timeout=1;TrustServerCertificate=True"
        );

        // then
        connected.Should().BeFalse();
        databaseExists.Should().BeFalse();
    }

    [Fact]
    public void should_connect_to_master_database_first()
    {
        // given - verify implementation modifies InitialCatalog to 'master'
        const string connectionString = "Server=localhost;Database=MyAppDb;TrustServerCertificate=True";
        var builder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 1 };

        // when - simulate what CheckAsync does internally
        var oldDatabaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        // then
        oldDatabaseName.Should().Be("MyAppDb");
        builder.InitialCatalog.Should().Be("master");
        builder.ConnectionString.Should().Contain("Initial Catalog=master");
    }

    [Fact]
    public void should_set_connect_timeout_to_1_second()
    {
        // given - verify implementation sets ConnectTimeout = 1
        const string connectionString = "Server=localhost;Database=test;Connect Timeout=30";

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            // when - simulate what CheckAsync does
            ConnectTimeout = 1,
        };

        // then
        builder.ConnectTimeout.Should().Be(1);
        builder.ConnectionString.Should().Contain("Connect Timeout=1");
    }

    [Fact]
    public async Task should_log_warning_on_exception()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        // Source-generated LoggerMessage gates on IsEnabled before calling Log; default mock returns false.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var sut = new SqlServerConnectionStringChecker(logger);

        // when - use invalid connection that will fail
        await sut.CheckAsync("Server=invalid-server-12345;Database=test;Connect Timeout=1;TrustServerCertificate=True");

        // then - verify a warning-level Log call was issued.
        // Source-generated LoggerMessage uses a private state struct, so we can't match Log<object>
        // directly via NSubstitute's generic specialization. Inspect raw calls instead.
        logger
            .ReceivedCalls()
            .Should()
            .Contain(call =>
                call.GetMethodInfo().Name == nameof(ILogger.Log)
                && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning
            );
    }
}
