// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.Sqlite;
using Microsoft.Extensions.Logging;

namespace Tests.Sqlite;

/// <summary>
/// Unit tests for <see cref="SqliteConnectionStringChecker"/>.
/// These are structural tests; integration tests require actual database.
/// </summary>
public sealed class SqliteConnectionStringCheckerTests
{
    [Fact]
    public void should_implement_IConnectionStringChecker()
    {
        // given
        var logger = Substitute.For<ILogger<SqliteConnectionStringChecker>>();

        // when
        var sut = new SqliteConnectionStringChecker(logger);

        // then
        sut.Should().BeAssignableTo<IConnectionStringChecker>();
    }

    [Fact]
    public async Task should_return_true_for_in_memory_connection()
    {
        // given
        var logger = Substitute.For<ILogger<SqliteConnectionStringChecker>>();
        var sut = new SqliteConnectionStringChecker(logger);

        // when
        var (connected, databaseExists) = await sut.CheckAsync("Data Source=:memory:");

        // then
        connected.Should().BeTrue();
        databaseExists.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_for_invalid_path()
    {
        // given
        var logger = Substitute.For<ILogger<SqliteConnectionStringChecker>>();
        var sut = new SqliteConnectionStringChecker(logger);

        // when
        var (connected, databaseExists) = await sut.CheckAsync(
            "Data Source=/nonexistent/path/that/should/not/exist/db.sqlite;Mode=ReadOnly"
        );

        // then
        connected.Should().BeFalse();
        databaseExists.Should().BeFalse();
    }

    [Fact]
    public async Task should_log_warning_on_exception()
    {
        // given
        var logger = Substitute.For<ILogger<SqliteConnectionStringChecker>>();
        // Source-generated LoggerMessage gates on IsEnabled before calling Log; default mock returns false.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var sut = new SqliteConnectionStringChecker(logger);

        // when - use invalid path that will throw
        await sut.CheckAsync("Data Source=/nonexistent/path/that/should/not/exist/db.sqlite;Mode=ReadOnly");

        // then - verify a warning-level Log call was issued.
        // Source-generated LoggerMessage uses a private state struct, so we can't match Log<object>
        // directly via NSubstitute's generic specialization. Inspect raw calls instead.
        logger
            .ReceivedCalls()
            .Should()
            .ContainSingle(call =>
                call.GetMethodInfo().Name == nameof(ILogger.Log)
                && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning
            );
    }
}
