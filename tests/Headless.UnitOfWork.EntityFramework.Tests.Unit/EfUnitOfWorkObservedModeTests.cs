// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Observed-mode <c>Enlist(db, tx)</c> against SQLite in-memory: the caller commits/rolls back its own
/// transaction; <c>CompleteAsync</c> only drains. The forgotten-completion warning fires exactly when the
/// unit was neither completed nor rolled back and the transaction had already finished.
/// </summary>
public sealed class EfUnitOfWorkObservedModeTests : TestBase
{
    [Fact]
    public async Task should_drain_on_completed_when_the_caller_commits_its_own_transaction()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var drained = 0;

        await using (var transaction = await session.Db.Database.BeginTransactionAsync(AbortToken))
        {
            await using var unitOfWork = session.Factory.Enlist(session.Db, transaction);

            unitOfWork.Resource.Should().BeAssignableTo<IRelationalUnitOfWorkResource>();
            unitOfWork.Resource!.IsOwned.Should().BeFalse("observed mode never owns the transaction");
            await session.Db.Probes.AddAsync(new ProbeRow { Name = "observed" }, AbortToken);
            await session.Db.SaveChangesAsync(AbortToken);

            unitOfWork.OnCompleted(() =>
            {
                drained++;

                return ValueTask.CompletedTask;
            });

            await transaction.CommitAsync(AbortToken);
            await unitOfWork.CompleteAsync(AbortToken);
        }

        drained.Should().Be(1, "CompleteAsync in observed mode drains without committing");
        (await host.CountProbeRowsAsync()).Should().Be(1, "the caller's own commit made the row durable");
    }

    [Fact]
    public async Task should_not_log_when_the_caller_rolled_back_and_the_unit_rolled_back_explicitly()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        await using (var transaction = await session.Db.Database.BeginTransactionAsync(AbortToken))
        {
            var unitOfWork = session.Factory.Enlist(session.Db, transaction);

            await session.Db.Probes.AddAsync(new ProbeRow { Name = "discarded" }, AbortToken);
            await session.Db.SaveChangesAsync(AbortToken);
            await transaction.RollbackAsync(AbortToken);
            await unitOfWork.RollbackAsync();
            await unitOfWork.DisposeAsync();
        }

        session.Logs.Should().BeEmpty("an explicit RollbackAsync suppresses the forgotten-completion warning");
        (await host.CountProbeRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_log_the_forgotten_completion_warning_when_disposed_after_a_finished_transaction()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        await using (var transaction = await session.Db.Database.BeginTransactionAsync(AbortToken))
        {
            var unitOfWork = session.Factory.Enlist(session.Db, transaction);

            await session.Db.Probes.AddAsync(new ProbeRow { Name = "committed" }, AbortToken);
            await session.Db.SaveChangesAsync(AbortToken);
            await transaction.CommitAsync(AbortToken);

            // Dispose without CompleteAsync/RollbackAsync after the transaction finished: the manager logs.
            await unitOfWork.DisposeAsync();
        }

        var warning = session.Logs.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("was disposed without CompleteAsync or RollbackAsync");
        warning.Message.Should().Contain("durable rows will be recovered by the relay");
    }

    [Fact]
    public async Task should_bind_the_enlisted_unit_to_the_context_and_evict_it_after_rollback()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        await using var transaction = await session.Db.Database.BeginTransactionAsync(AbortToken);
        var unitOfWork = session.Factory.Enlist(session.Db, transaction);

        session.Db.UnitOfWork().Should().BeSameAs(unitOfWork, "Enlist records the binding");

        await unitOfWork.RollbackAsync();
        await unitOfWork.DisposeAsync();

        session.Db.UnitOfWork().Should().BeNull("a terminal unit is evicted from the binding");
    }

    [Fact]
    public async Task should_refuse_a_second_enlist_on_a_context_that_already_carries_a_live_unit()
    {
        // Two units observing one transaction is the same misuse as two owning it: the second enlist is refused
        // with the join remedy, before it registers anything, and the first unit is left as it was.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        await using var transaction = await session.Db.Database.BeginTransactionAsync(AbortToken);
        await using var first = session.Factory.Enlist(session.Db, transaction);

        var act = () => session.Factory.Enlist(session.Db, transaction);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*DbContext already carries an active unit of work*RunAsync(db*db.UnitOfWork()*");
        first.State.Should().Be(UnitOfWorkState.Active);
        session.Db.UnitOfWork().Should().BeSameAs(first);
    }

    [Fact]
    public async Task should_refuse_an_owning_begin_on_a_context_that_carries_an_observed_unit()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        await using var transaction = await session.Db.Database.BeginTransactionAsync(AbortToken);
        await using var observed = session.Factory.Enlist(session.Db, transaction);

        var act = () => session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*DbContext already carries an active unit of work*RunAsync(db*"
        );
        observed.State.Should().Be(UnitOfWorkState.Active);
        session.Db.Database.CurrentTransaction.Should().BeSameAs(transaction, "the refused begin opened nothing");
    }
}
