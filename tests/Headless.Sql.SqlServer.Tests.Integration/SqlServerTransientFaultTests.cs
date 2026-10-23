// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Tests.TestSetup;

namespace Tests;

/// <summary>
/// How the shared replay classifier reads what a real SqlClient raises. A command timeout can fire after the
/// statement completed on the server, so a replay could run it twice; a refused connection never reached the server,
/// so a fresh connection may cure it.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerTransientFaultTests(SqlServerTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_not_treat_a_command_timeout_as_transient()
    {
        // given
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "WAITFOR DELAY '00:00:05'";
        command.CommandTimeout = 1;

        // when
        var timeout = (
            await FluentActions
                .Invoking(() => command.ExecuteNonQueryAsync(AbortToken))
                .Should()
                .ThrowAsync<SqlException>()
        ).Which;

        // then
        timeout.Number.Should().Be(-2, "SqlClient reports a command timeout as error -2");
        RelationalTransientFaults.IsTransient(timeout, CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public async Task should_treat_a_refused_connection_as_transient()
    {
        // given: a port nothing listens on
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            DataSource = "127.0.0.1,1",
            ConnectTimeout = 3,
            Pooling = false,
        };
        await using var connection = new SqlConnection(builder.ConnectionString);

        // when
        var refused = (
            await FluentActions.Invoking(() => connection.OpenAsync(AbortToken)).Should().ThrowAsync<SqlException>()
        ).Which;

        // then
        RelationalTransientFaults.IsTransient(refused, CancellationToken.None).Should().BeTrue();
    }
}
