// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

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
        await using (var unitOfWork = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken))
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

    [Fact]
    public async Task should_roll_back_when_the_owned_unit_is_disposed_without_complete()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var unitOfWork = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);
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

        await using var unitOfWork = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);
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

        var act = () => session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken).AsTask();

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

        var act = () => session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken).AsTask();

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

        var unitOfWork = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);

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

        await using var first = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);

        var act = () => session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken).AsTask();

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

        var first = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);
        await first.RollbackAsync();
        await first.DisposeAsync();

        await using var second = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);

        second.Should().NotBeSameAs(first);
        session.Db.UnitOfWork().Should().BeSameAs(second, "the binding follows the live unit");
    }
}
