// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.PostgreSql;

/// <summary>
/// Unit tests for <see cref="NpgsqlConnectionStringChecker"/>.
/// These are structural tests; integration tests require actual database.
/// </summary>
public sealed class NpgsqlConnectionStringCheckerTests : TestBase
{
    [Fact]
    public async Task should_return_false_and_log_warning_for_invalid_connection_string()
    {
        // given
        var logger = Substitute.For<ILogger<NpgsqlConnectionStringChecker>>();
        // Source-generated LoggerMessage gates on IsEnabled before calling Log; default mock returns false.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var sut = new NpgsqlConnectionStringChecker(logger);

        // when - use invalid host to trigger exception
        var (connected, databaseExists) = await sut.CheckAsync(
            "Host=invalid-host-that-does-not-exist;Database=test;Timeout=1",
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
