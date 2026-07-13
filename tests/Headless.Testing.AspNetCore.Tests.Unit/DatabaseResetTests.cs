// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Testing.AspNetCore;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DatabaseResetTests : TestBase
{
    [Fact]
    public async Task should_throw_when_connection_not_open_on_create()
    {
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);

        Func<Task> act = async () => await DatabaseReset.CreateAsync(connection);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("*open*");
    }

    [Fact]
    public async Task should_cancel_create_when_token_is_cancelled()
    {
        await using var connection = await TestSqliteConnection.CreateAsync(AbortToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = async () => await DatabaseReset.CreateAsync(connection, cancellationToken: cancellation.Token);

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_cancel_reset_when_token_is_cancelled()
    {
        await using var connection = await TestSqliteConnection.CreateAsync(AbortToken);
        var reset = await DatabaseReset.CreateAsync(
            connection,
            new DatabaseResetOptions { DbAdapter = Respawn.DbAdapter.Sqlite },
            AbortToken
        );
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = async () => await reset.ResetAsync(connection, cancellation.Token);

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_close_connection_when_cancelling_running_operation()
    {
        var connection = Substitute.For<DbConnection>();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.When(x => x.Close()).Do(_ => operation.TrySetException(Substitute.For<DbException>()));
        var cancellation = new CancellationTokenSource();

        var task = DatabaseResetOperation.RunAsync(connection, () => operation.Task, cancellation.Token);
        await cancellation.CancelAsync();

        var act = async () => await task;

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
        cancellation.Dispose();
        connection
            .ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(DbConnection.Close))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_unregister_cancellation_after_operation_completes()
    {
        var connection = Substitute.For<DbConnection>();
        using var cancellation = new CancellationTokenSource();

        await DatabaseResetOperation.RunAsync(connection, () => Task.CompletedTask, cancellation.Token);
        await cancellation.CancelAsync();

        connection.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(DbConnection.Close));
    }
}
