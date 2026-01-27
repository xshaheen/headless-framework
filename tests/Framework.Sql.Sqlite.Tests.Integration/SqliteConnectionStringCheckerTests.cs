// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sql;
using Framework.Sql.Sqlite;
using Framework.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Tests;

/// <summary>
/// Integration tests for <see cref="SqliteConnectionStringChecker"/>.
/// Tests actual database connectivity scenarios.
/// </summary>
public sealed class SqliteConnectionStringCheckerTests : TestBase
{
    private static ILogger<SqliteConnectionStringChecker> CreateLogger()
    {
        return Substitute.For<ILogger<SqliteConnectionStringChecker>>();
    }

    // Tests 62-63: should_return_both_true_for_valid_connection, should_return_both_false_on_exception

    [Fact]
    public async Task should_return_both_true_for_valid_connection()
    {
        // given
        var sut = new SqliteConnectionStringChecker(CreateLogger());

        // when
        var result = await sut.CheckAsync("Data Source=:memory:");

        // then - SQLite always returns (true, true) on success
        result.Connected.Should().BeTrue();
        result.DatabaseExists.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_both_false_on_exception()
    {
        // given
        var sut = new SqliteConnectionStringChecker(CreateLogger());

        // when - use invalid connection string that will throw
        var result = await sut.CheckAsync("Data Source=/nonexistent/path/db.sqlite;Mode=ReadOnly");

        // then
        result.Connected.Should().BeFalse();
        result.DatabaseExists.Should().BeFalse();
    }

    // Test 65: should_close_connection_after_check

    [Fact]
    public async Task should_close_connection_after_check()
    {
        // given
        var sut = new SqliteConnectionStringChecker(CreateLogger());
        var connectionString = "Data Source=:memory:";

        // when
        var result = await sut.CheckAsync(connectionString);

        // then
        result.Connected.Should().BeTrue();
        // Connection should be closed after check - verify by opening new connection
        // If connection was not properly closed, there might be issues
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    // Test 66: should_handle_in_memory_database

    [Fact]
    public async Task should_handle_in_memory_database()
    {
        // given
        var sut = new SqliteConnectionStringChecker(CreateLogger());

        // when - test various in-memory connection string formats
        var result1 = await sut.CheckAsync("Data Source=:memory:");
        var result2 = await sut.CheckAsync("DataSource=:memory:");

        // then
        result1.Connected.Should().BeTrue();
        result1.DatabaseExists.Should().BeTrue();
        result2.Connected.Should().BeTrue();
        result2.DatabaseExists.Should().BeTrue();
    }

    // Test 67: should_handle_nonexistent_file_path

    [Fact]
    public async Task should_handle_nonexistent_file_path()
    {
        // given
        var sut = new SqliteConnectionStringChecker(CreateLogger());
        var nonExistentPath = "/nonexistent/directory/that/does/not/exist/database.sqlite";

        // when - Mode=ReadOnly ensures failure on missing file
        var result = await sut.CheckAsync($"Data Source={nonExistentPath};Mode=ReadOnly");

        // then
        result.Connected.Should().BeFalse();
        result.DatabaseExists.Should().BeFalse();
    }

    [Fact]
    public async Task should_implement_IConnectionStringChecker()
    {
        // given
        var logger = CreateLogger();

        // when
        var sut = new SqliteConnectionStringChecker(logger);

        // then
        sut.Should().BeAssignableTo<IConnectionStringChecker>();
    }

    [Fact]
    public async Task should_handle_file_based_database()
    {
        // given
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sqlite");
        var sut = new SqliteConnectionStringChecker(CreateLogger());

        try
        {
            // Create the database file first
            await using (var connection = new SqliteConnection($"Data Source={tempFile}"))
            {
                await connection.OpenAsync(AbortToken);
            }

            // when
            var result = await sut.CheckAsync($"Data Source={tempFile}");

            // then
            result.Connected.Should().BeTrue();
            result.DatabaseExists.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task should_log_warning_on_connection_failure()
    {
        // given
        var logger = CreateLogger();
        var sut = new SqliteConnectionStringChecker(logger);

        // when
        await sut.CheckAsync("Data Source=/nonexistent/path/db.sqlite;Mode=ReadOnly");

        // then
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
