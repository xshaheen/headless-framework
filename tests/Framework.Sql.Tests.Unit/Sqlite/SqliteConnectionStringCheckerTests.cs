// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sql;
using Framework.Sql.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;

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
        var result = await sut.CheckAsync("Data Source=:memory:");

        // then
        result.Connected.Should().BeTrue();
        result.DatabaseExists.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_for_invalid_path()
    {
        // given
        var logger = Substitute.For<ILogger<SqliteConnectionStringChecker>>();
        var sut = new SqliteConnectionStringChecker(logger);

        // when
        var result = await sut.CheckAsync(
            "Data Source=/nonexistent/path/that/should/not/exist/db.sqlite;Mode=ReadOnly"
        );

        // then
        result.Connected.Should().BeFalse();
        result.DatabaseExists.Should().BeFalse();
    }

    [Fact]
    public async Task should_log_warning_on_exception()
    {
        // given
        var logger = Substitute.For<ILogger<SqliteConnectionStringChecker>>();
        var sut = new SqliteConnectionStringChecker(logger);

        // when - use invalid path that will throw
        await sut.CheckAsync("Data Source=/nonexistent/path/that/should/not/exist/db.sqlite;Mode=ReadOnly");

        // then - verify LogWarning was called
        logger
            .Received(1)
            .Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }
}
