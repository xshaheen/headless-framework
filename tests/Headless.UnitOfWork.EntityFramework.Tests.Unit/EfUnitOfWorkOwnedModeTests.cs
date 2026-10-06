// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Owned-mode <c>BeginAsync(db)</c> against SQLite in-memory: complete commits and drains, dispose rolls
/// back, explicit rollback runs OnFailed, and the context binding is recorded and evicted.
/// </summary>
public sealed class EfUnitOfWorkOwnedModeTests : TestBase
{
    [Fact]
    public async Task should_commit_and_drain_when_the_owned_unit_completes()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var drained = 0;
        await using (var unitOfWork = await session.Factory.BeginAsync(session.Db, AbortToken))
        {
            unitOfWork.Resource.Should().BeAssignableTo<IRelationalUnitOfWorkResource>();
            unitOfWork.State.Should().Be(UnitOfWorkState.Active);
            await session.Db.Probes.AddAsync(new ProbeRow { Name = "committed" }, AbortToken);
            await session.Db.SaveChangesAsync(AbortToken);

            unitOfWork.OnCompleted(() =>
            {
                drained++;

                return ValueTask.CompletedTask;
            });

            await unitOfWork.CompleteAsync(AbortToken);
        }

        drained.Should().Be(1, "the completion drain runs after the owned transaction commits");
        (await host.CountProbeRowsAsync()).Should().Be(1, "the row is durable in a fresh context");
        session.Db.Database.CurrentTransaction.Should().BeNull("the owned transaction is disposed with the unit");
    }

    [Theory]
    [InlineData("begin", null, IsolationLevel.ReadCommitted)]
    [InlineData("begin", IsolationLevel.Serializable, IsolationLevel.Serializable)]
    [InlineData("run", null, IsolationLevel.ReadCommitted)]
    [InlineData("run", IsolationLevel.Serializable, IsolationLevel.Serializable)]
    public async Task should_begin_at_read_committed_unless_the_caller_passes_an_isolation_level(
        string entryPoint,
        IsolationLevel? passed,
        IsolationLevel expected
    )
    {
        // SQLite reports Serializable for every transaction it opens, so the level EF was asked for is read at the
        // transaction-starting interception point instead of from the opened transaction.
        var requested = new List<IsolationLevel>();
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.AddInterceptors(new IsolationLevelRecorder(requested))
        );
        await using var session = host.CreateSession();
        requested.Clear(); // Schema creation in the host opens a transaction of its own.

        if (string.Equals(entryPoint, "begin", StringComparison.Ordinal))
        {
            await using var unitOfWork = passed is { } level
                ? await session.Factory.BeginAsync(session.Db, level, AbortToken)
                : await session.Factory.BeginAsync(session.Db, AbortToken);
        }
        else
        {
            Func<IUnitOfWork, CancellationToken, Task> operation = static (_, _) => Task.CompletedTask;

            await (
                passed is { } level
                    ? session.Factory.RunAsync(session.Db, operation, level, AbortToken)
                    : session.Factory.RunAsync(session.Db, operation, AbortToken)
            );
        }

        requested.Should().Equal(expected);
    }

    [Fact]
    public async Task should_roll_back_when_the_owned_unit_is_disposed_without_complete()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var unitOfWork = await session.Factory.BeginAsync(session.Db, AbortToken);
        await session.Db.Probes.AddAsync(new ProbeRow { Name = "rolled-back" }, AbortToken);
        await session.Db.SaveChangesAsync(AbortToken);

        await unitOfWork.DisposeAsync();

        (await host.CountProbeRowsAsync()).Should().Be(0, "dispose without complete rolls the owned transaction back");
    }

    [Fact]
    public async Task should_roll_back_and_run_on_failed_when_the_owned_unit_rolls_back_explicitly()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        await using var unitOfWork = await session.Factory.BeginAsync(session.Db, AbortToken);
        await session.Db.Probes.AddAsync(new ProbeRow { Name = "discarded" }, AbortToken);
        await session.Db.SaveChangesAsync(AbortToken);
        UnitOfWorkFailure? failure = null;
        unitOfWork.OnFailed(f =>
        {
            failure = f;

            return ValueTask.CompletedTask;
        });

        await unitOfWork.RollbackAsync();

        (await host.CountProbeRowsAsync()).Should().Be(0);
        unitOfWork.State.Should().Be(UnitOfWorkState.Failed);
        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.RolledBack);
    }

    [Fact]
    public async Task should_throw_the_catalogued_message_when_the_context_already_has_a_transaction()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        await using var transaction = await session.Db.Database.BeginTransactionAsync(AbortToken);

        var act = () => session.Factory.BeginAsync(session.Db, AbortToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("The DbContext already has an active transaction.")
            .And.Contain("call IUnitOfWorkFactory.Enlist(db, transaction)");
    }

    [Fact]
    public async Task should_throw_the_run_async_remedy_when_begin_runs_under_a_retrying_execution_strategy()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        await using var session = host.CreateSession();

        var act = () => session.Factory.BeginAsync(session.Db, AbortToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("does not support user-initiated transactions")
            .And.Contain("Use IUnitOfWorkFactory.RunAsync(db");
    }

    [Fact]
    public async Task should_bind_the_unit_to_the_context_and_evict_it_after_completion()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var unitOfWork = await session.Factory.BeginAsync(session.Db, AbortToken);

        session.Db.UnitOfWork().Should().BeSameAs(unitOfWork, "BeginAsync(db) records the binding");

        await unitOfWork.CompleteAsync(AbortToken);
        await unitOfWork.DisposeAsync();

        session.Db.UnitOfWork().Should().BeNull("a terminal unit is evicted from the binding");
    }

    [Fact]
    public async Task should_refuse_a_second_begin_on_a_context_that_already_carries_a_live_unit()
    {
        // An owning begin over someone else's transaction has no honest semantics: the callee joins through
        // RunAsync(db, …) or is handed the unit (db.UnitOfWork()) instead of opening a second one.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        await using var first = await session.Factory.BeginAsync(session.Db, AbortToken);

        var act = () => session.Factory.BeginAsync(session.Db, AbortToken).AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*already carries an active unit of work*db.UnitOfWork()*");
        first.State.Should().Be(UnitOfWorkState.Active, "the refused begin leaves the live unit untouched");
        session.Db.UnitOfWork().Should().BeSameAs(first);
    }

    [Fact]
    public async Task should_allow_a_new_begin_once_the_bound_unit_reached_a_terminal_state()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var first = await session.Factory.BeginAsync(session.Db, AbortToken);
        await first.RollbackAsync();
        await first.DisposeAsync();

        await using var second = await session.Factory.BeginAsync(session.Db, AbortToken);

        second.Should().NotBeSameAs(first);
        session.Db.UnitOfWork().Should().BeSameAs(second, "the binding follows the live unit");
    }

    private sealed class IsolationLevelRecorder(List<IsolationLevel> requested) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            requested.Add(eventData.IsolationLevel);

            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }
}
