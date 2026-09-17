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
        await using (var unitOfWork = await session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken))
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

        var unitOfWork = await session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken);
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

        await using var unitOfWork = await session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken);
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

        var act = () => session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("The DbContext already has an active transaction.")
            .And.Contain("call IUnitOfWorkManager.Enlist(db, transaction)");
    }

    [Fact]
    public async Task should_throw_the_run_async_remedy_when_begin_runs_under_a_retrying_execution_strategy()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        await using var session = host.CreateSession();

        var act = () => session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("does not support user-initiated transactions")
            .And.Contain("Use IUnitOfWorkManager.RunAsync(db");
    }

    [Fact]
    public async Task should_bind_the_unit_to_the_context_and_evict_it_after_completion()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var unitOfWork = await session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken);

        DbContextUnitOfWork.Find(session.Db).Should().BeSameAs(unitOfWork, "BeginAsync(db) records the binding");

        await unitOfWork.CompleteAsync(AbortToken);
        await unitOfWork.DisposeAsync();

        DbContextUnitOfWork.Find(session.Db).Should().BeNull("a terminal unit is evicted from the binding");
    }

    [Fact]
    public async Task should_return_a_child_when_begin_runs_again_on_the_same_context()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        await using var root = await session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken);
        await using var child = await session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken);

        child.Should().NotBeSameAs(root, "a second begin on the same resource joins as a child");
        session.Manager.Current.Should().BeSameAs(child, "Current is the innermost unit");
        root.State.Should().Be(UnitOfWorkState.Active);
        session
            .Db.Database.CurrentTransaction.Should()
            .NotBeNull("the root's transaction stays open while the root unit is active");
        child.Resource.Should().BeSameAs(root.Resource, "the child joined the root's resource — no second transaction");
    }

    [Fact]
    public async Task should_keep_the_root_bound_to_the_context_across_a_nested_child()
    {
        // The save pipeline resolves the unit through the context binding; a joined begin must not rebind the
        // context to its child view, or the next save after the child completes would adopt a stale handle.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        await using var root = await session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken);
        var child = await session.Manager.BeginAsync(session.Db, cancellationToken: AbortToken);

        DbContextUnitOfWork.Find(session.Db).Should().BeSameAs(root, "a joined begin never rebinds the context");

        using (session.Manager.Adopt(root))
        {
            session.Manager.Current.Should().BeSameAs(child, "adopting the bound root under its child is re-entrant");
        }

        await child.CompleteAsync(AbortToken);
        await child.DisposeAsync();

        DbContextUnitOfWork.Find(session.Db).Should().BeSameAs(root);
        session.Manager.Current.Should().BeSameAs(root);
    }
}
