// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The owned and observed contracts of the SqlClient provider against a real SQL Server: an owned unit begins
/// and commits its own transaction; an observed unit is completed by the caller after its commit, and a dispose
/// that arrives without a completion verb after a completed transaction is logged by the manager as forgotten.
/// </summary>
[Collection<SqlServerUnitOfWorkFixture>]
public sealed class SqlServerUnitOfWorkTests(SqlServerUnitOfWorkFixture fixture) : TestBase
{
    private const string _ManagerCategory = "Headless.UnitOfWork.UnitOfWorkManager";

    [Fact]
    public async Task should_commit_the_row_when_an_owned_unit_completes()
    {
        await fixture.ResetAsync(AbortToken);
        var logs = new CapturingLoggerProvider();
        await using var provider = SqlServerUnitOfWorkFixture.BuildProvider(logs);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        var calls = 0;

        await using (var unitOfWork = await manager.BeginAsync(connection, cancellationToken: AbortToken))
        {
            unitOfWork.OnCompleted(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });
            await SqlServerUnitOfWorkFixture.InsertProbeRowAsync(
                connection,
                _Transaction(unitOfWork),
                "owned",
                AbortToken
            );
            await unitOfWork.CompleteAsync(AbortToken);

            unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
        }

        calls.Should().Be(1);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
        connection.State.Should().Be(System.Data.ConnectionState.Closed, "the provider closes a connection it opened");
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_roll_the_row_back_when_an_owned_unit_is_disposed_without_completing()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = SqlServerUnitOfWorkFixture.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        UnitOfWorkFailure? failure = null;

        await using (var unitOfWork = await manager.BeginAsync(connection, cancellationToken: AbortToken))
        {
            unitOfWork.OnFailed(f =>
            {
                failure = f;

                return ValueTask.CompletedTask;
            });
            await SqlServerUnitOfWorkFixture.InsertProbeRowAsync(
                connection,
                _Transaction(unitOfWork),
                "abandoned",
                AbortToken
            );
        }

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0);
        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.Abandoned);
        manager.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_drain_once_when_the_caller_completes_an_observed_unit_after_commit()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = SqlServerUnitOfWorkFixture.BuildProvider(logs);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var calls = 0;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        await using (var unitOfWork = manager.Enlist(connection, transaction))
        {
            unitOfWork.OnCompleted(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            await _TouchAsync(connection, transaction);
            await transaction.CommitAsync(AbortToken);
            await unitOfWork.CompleteAsync(AbortToken);
        }

        calls.Should().Be(1);
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_warn_and_discard_when_an_observed_unit_is_disposed_without_a_completion_verb_after_commit()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = SqlServerUnitOfWorkFixture.BuildProvider(logs);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var calls = 0;
        IUnitOfWork unit;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        await using (var unitOfWork = manager.Enlist(connection, transaction))
        {
            unit = unitOfWork;
            unitOfWork.OnCompleted(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            await _TouchAsync(connection, transaction);
            await transaction.CommitAsync(AbortToken);
            // Forgotten completion: SqlClient exposes no commit edge to observe for the caller.
        }

        calls.Should().Be(0);
        unit.State.Should().Be(UnitOfWorkState.Failed);
        logs.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && e.Category == _ManagerCategory)
            .Which.Message.Should()
            .Contain("disposed without CompleteAsync or RollbackAsync");
    }

    [Fact]
    public async Task should_stay_silent_when_the_caller_rolls_an_observed_unit_back_explicitly()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = SqlServerUnitOfWorkFixture.BuildProvider(logs);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        UnitOfWorkFailure? failure = null;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        await using (var unitOfWork = manager.Enlist(connection, transaction))
        {
            unitOfWork.OnFailed(f =>
            {
                failure = f;

                return ValueTask.CompletedTask;
            });

            await _TouchAsync(connection, transaction);
            await transaction.RollbackAsync(AbortToken);
            await unitOfWork.RollbackAsync();
        }

        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.RolledBack);
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_not_warn_when_an_observed_unit_is_disposed_before_the_transaction_completes()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = SqlServerUnitOfWorkFixture.BuildProvider(logs);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        await using (manager.Enlist(connection, transaction))
        {
            await _TouchAsync(connection, transaction);
            // The operation failed: the unit is disposed while the transaction is still open.
        }

        await transaction.RollbackAsync(AbortToken);

        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    private static SqlTransaction _Transaction(IUnitOfWork unitOfWork)
    {
        return (SqlTransaction)((IRelationalUnitOfWorkResource)unitOfWork.Resource!).Transaction;
    }

    private static async Task _TouchAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = new SqlCommand("SELECT 1", connection, transaction);
        await command.ExecuteScalarAsync(AbortToken);
    }
}
