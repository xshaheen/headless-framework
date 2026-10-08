// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.SqlServer;

/// <summary>
/// Unit tests for <see cref="SqlServerConnectionStringChecker"/>.
/// These are structural tests; integration tests require actual database.
/// </summary>
public sealed class SqlServerConnectionStringCheckerTests : TestBase
{
    [Fact]
    public async Task should_return_false_and_log_warning_for_invalid_connection_string()
    {
        // given
        var logger = Substitute.For<ILogger<SqlServerConnectionStringChecker>>();
        // Source-generated LoggerMessage gates on IsEnabled before calling Log; default mock returns false.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var sut = new SqlServerConnectionStringChecker(logger);

        // when - use invalid connection that will fail
        var (connected, databaseExists) = await sut.CheckAsync(
            "Server=invalid-host-that-does-not-exist;Database=test;Connect Timeout=1;TrustServerCertificate=True",
            AbortToken
        );

        // then - the check reports failure and a warning-level Log call was issued.
        connected.Should().BeFalse();
        databaseExists.Should().BeFalse();
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
